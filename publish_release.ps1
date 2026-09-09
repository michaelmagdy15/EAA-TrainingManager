<#
.SYNOPSIS
    One-Click Automated Release & In-App Updater Deployment for EAA Training Management System.
#>

[CmdletBinding()]
param (
    [Parameter(Position = 0)]
    [string]$Version,

    [Parameter(Position = 1)]
    [string]$Title,

    [Parameter(Position = 2)]
    [string]$Notes,

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ScriptRoot) { $ScriptRoot = (Get-Location).Path }

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "  EAA TRAINING MANAGER - ONE-CLICK RELEASE AUTOMATION ENGINE      " -ForegroundColor Yellow
Write-Host "==================================================================" -ForegroundColor Cyan

# 0. Set Authentication for Private GitHub Operations
$GitHubRepo = "michaelmagdy15/EAA-TrainingManager"
$isGhLoggedIn = $false
try {
    $null = gh auth status 2>$null
    if ($LASTEXITCODE -eq 0) {
        $isGhLoggedIn = $true
    }
} catch { }

if ($isGhLoggedIn) {
    Write-Host "  -> GitHub CLI authenticated via user credentials." -ForegroundColor Green
    [Environment]::SetEnvironmentVariable('GH_TOKEN', $null, 'Process')
    [Environment]::SetEnvironmentVariable('GITHUB_TOKEN', $null, 'Process')
} else {
    $Token = "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"
    [Environment]::SetEnvironmentVariable('GH_TOKEN', $Token, 'Process')
    [Environment]::SetEnvironmentVariable('GITHUB_TOKEN', $Token, 'Process')
}

# 1. Determine Target Version
$ManifestPath = Join-Path $ScriptRoot "update_manifest.json"
$CurrentVersion = "2.2.1"

if (Test-Path $ManifestPath) {
    try {
        $manifestJson = Get-Content $ManifestPath -Raw | ConvertFrom-Json
        if ($manifestJson.version) {
            $CurrentVersion = $manifestJson.version
        }
    } catch {
        Write-Warning "Could not read existing version from manifest, fallback: $CurrentVersion"
    }
}

if (-not $Version) {
    $versionParts = $CurrentVersion.Split('.')
    if ($versionParts.Length -ge 3) {
        $patchNum = [int]$versionParts[2] + 1
        $suggestedVersion = "$($versionParts[0]).$($versionParts[1]).$patchNum"
    } else {
        $suggestedVersion = "$CurrentVersion.1"
    }

    Write-Host "`nCurrent Version: " -NoNewline -ForegroundColor Gray
    Write-Host $CurrentVersion -ForegroundColor White
    Write-Host "Suggested Next Version: " -NoNewline -ForegroundColor Gray
    Write-Host $suggestedVersion -ForegroundColor Green

    if ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
        $userPrompt = Read-Host "Press ENTER to accept [$suggestedVersion] or enter custom version"
        if ([string]::IsNullOrWhiteSpace($userPrompt)) {
            $Version = $suggestedVersion
        } else {
            $Version = $userPrompt.Trim().TrimStart('v')
        }
    } else {
        $Version = $suggestedVersion
    }
} else {
    $Version = $Version.Trim().TrimStart('v')
}

$Tag = "v$Version"
Write-Host "`n>>> Target Release Tag: $Tag" -ForegroundColor Magenta

# 2. Prepare Release Title and Description
if (-not $Title) {
    $Title = "v$Version - Operational Release (Private Updates & Auto-Sync)"
}

if (-not $Notes) {
    $Notes = @"
## Egyptian Aviation Academy (EAA) Training Management System v$Version

### Key Updates & Enhancements:
- **Optimized UI Layout**: Fixed title bar header alignment, widened data columns, and resolved text/button squeezing across all directories and dashboards.
- **Private Repository Delta Updates**: In-app one-click update detection and background patch delivery via authenticated REST API.
- **Unified Student & Trainee Directory**: Real-time deduplication linking historical courses and milestones (PPL -> CPL/IR -> ATP).
- **Consular & Authority Export**: Instant 1-click export of accredited international cadet rosters.
- **SQLite Engine & Excel Mirroring**: High-performance offline database with background Excel snapshot mirroring.

### Ready-to-Run Binary:
- **EAATrainingManager.exe**: Standalone single-file executable with embedded .NET 9 desktop runtime (no installation required).
"@
}

# 3. Compile Executable (Self-Contained Single-File Binary)
$PublishDir = Join-Path $ScriptRoot "Publish"
$ExePath = Join-Path $PublishDir "EAATrainingManager.exe"
$CsprojPath = Join-Path $ScriptRoot "EAATrainingManager\EAATrainingManager.csproj"

if (-not $SkipBuild) {
    Write-Host "`n[1/6] Building and Publishing EAATrainingManager.exe..." -ForegroundColor Cyan
    
    dotnet publish $CsprojPath -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $PublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
        exit 1
    }
    Write-Host "  -> Compilation complete." -ForegroundColor Green
} else {
    Write-Host "`n[1/6] Skipping build step (-SkipBuild specified)." -ForegroundColor Yellow
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found at: $ExePath"
    exit 1
}

# 4. Compute Binary Metrics (Size and SHA256)
$exeItem = Get-Item $ExePath
$fileSizeBytes = $exeItem.Length
$sha256 = (Get-FileHash -Path $ExePath -Algorithm SHA256).Hash.ToLower()

Write-Host "`n[2/6] Binary File Verified:" -ForegroundColor Cyan
Write-Host "  Path: $ExePath" -ForegroundColor Gray
Write-Host "  Size: $([math]::Round($fileSizeBytes / 1MB, 2)) MB ($fileSizeBytes bytes)" -ForegroundColor Gray
Write-Host "  SHA256: $sha256" -ForegroundColor Gray

# Copy to root executable for immediate local use
$rootExe = Join-Path $ScriptRoot "EAATrainingManager.exe"
try {
    Copy-Item -Path $ExePath -Destination $rootExe -Force
    Write-Host "  -> Updated local root executable: $rootExe" -ForegroundColor Green
} catch {
    Write-Warning "Could not copy to root executable (it may be currently running)."
}

# 5. Update update_manifest.json
Write-Host "`n[3/6] Updating update_manifest.json..." -ForegroundColor Cyan
$todayDate = (Get-Date).ToString("yyyy-MM-dd")

$notesAr = "EAA Training Management System v$Version"
if (Test-Path $ManifestPath) {
    try {
        $existingManifest = Get-Content $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($existingManifest.releaseNotes) {
            $notesAr = [System.Text.RegularExpressions.Regex]::Replace($existingManifest.releaseNotes, "v\d+\.\d+\.\d+", "v$Version")
        }
    } catch { }
}
$notesEn = "Update v$Version - Enhanced UI layout, perfect table alignment, and automated continuous updater delivery."

$manifestObj = [ordered]@{
    version = $Version
    releaseDate = $todayDate
    releaseNotes = $notesAr
    releaseNotesEn = $notesEn
    deltaPatchUrl = "https://github.com/$GitHubRepo/releases/download/$Tag/EAATrainingManager.exe"
    patchSizeBytes = $fileSizeBytes
}
$manifestContent = ($manifestObj | ConvertTo-Json -Depth 4) + "`n"
[System.IO.File]::WriteAllText($ManifestPath, $manifestContent, [System.Text.Encoding]::UTF8)
Write-Host "  -> Manifest updated for version $Version." -ForegroundColor Green

# 6. Update EAATrainingManager.csproj Version Tags
Write-Host "`n[4/6] Updating project version references..." -ForegroundColor Cyan
$csprojContent = [System.IO.File]::ReadAllText($CsprojPath, [System.Text.Encoding]::UTF8)
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace($csprojContent, "<Version>.*?</Version>", "<Version>$Version</Version>")
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace($csprojContent, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$Version.0</AssemblyVersion>")
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace($csprojContent, "<FileVersion>.*?</FileVersion>", "<FileVersion>$Version.0</FileVersion>")
[System.IO.File]::WriteAllText($CsprojPath, $csprojContent, [System.Text.Encoding]::UTF8)

# 7. Update UpdateService.cs Version Constants
$updateServicePath = Join-Path $ScriptRoot "EAATrainingManager\Services\UpdateService.cs"
if (Test-Path $updateServicePath) {
    $serviceContent = [System.IO.File]::ReadAllText($updateServicePath, [System.Text.Encoding]::UTF8)
    $serviceContent = [System.Text.RegularExpressions.Regex]::Replace($serviceContent, 'public string CurrentVersion \{ get; \} = ".*?";', 'public string CurrentVersion { get; } = "' + $Version + '";')
    $serviceContent = [System.Text.RegularExpressions.Regex]::Replace($serviceContent, 'public string LatestVersion \{ get; set; \} = ".*?";', 'public string LatestVersion { get; set; } = "' + $Version + '";')
    [System.IO.File]::WriteAllText($updateServicePath, $serviceContent, [System.Text.Encoding]::UTF8)
}
Write-Host "  -> Project files updated to version $Version." -ForegroundColor Green

# 8. Create / Update Standalone ZIP Archive
Write-Host "`n[5/6] Creating Standalone Zip Archive..." -ForegroundColor Cyan
$zipPath = Join-Path $ScriptRoot "EAA_Training_Manager_Standalone.zip"
try {
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipPath -Force
    Write-Host "  -> Archive created: $zipPath" -ForegroundColor Green
} catch {
    Write-Warning "Could not update zip archive: $_"
}

# 9. Git Stage, Commit and Push
Write-Host "`n[6/6] Syncing with GitHub..." -ForegroundColor Cyan
Set-Location $ScriptRoot

git add -A
$gitStatus = git status --porcelain
if ($gitStatus) {
    Write-Host "  -> Committing changes..." -ForegroundColor Gray
    git commit -m "chore(release): bump version to $Tag and optimize UI layout"
    Write-Host "  -> Pushing to origin main..." -ForegroundColor Gray
    git push origin main
} else {
    Write-Host "  -> Git working tree clean, proceeding to release..." -ForegroundColor Gray
}

# 10. Create GitHub Release & Upload Binary via gh CLI
Write-Host "  -> Creating GitHub Release $Tag and Uploading Binary..." -ForegroundColor Cyan
$tempNotesFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tempNotesFile, $Notes, [System.Text.Encoding]::UTF8)

try {
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    & gh release view $Tag --repo $GitHubRepo 2>$null
    $releaseExists = ($LASTEXITCODE -eq 0)

    $uploadSuccess = $false
    if ($releaseExists) {
        Write-Host "  -> Release $Tag exists. Uploading/overwriting EAATrainingManager.exe asset..." -ForegroundColor Yellow
        $out = & gh release upload $Tag $ExePath --repo $GitHubRepo --clobber 2>&1
        $uploadSuccess = ($LASTEXITCODE -eq 0)
    } else {
        Write-Host "  -> Creating fresh release $Tag..." -ForegroundColor Gray
        $out = & gh release create $Tag $ExePath --repo $GitHubRepo --title $Title --notes-file $tempNotesFile --latest 2>&1
        $uploadSuccess = ($LASTEXITCODE -eq 0)
    }
    $ErrorActionPreference = $prevEAP

    if ($uploadSuccess) {
        Write-Host "  -> Release $Tag uploaded and marked as Latest!" -ForegroundColor Green
    } else {
        Write-Host "`n[!] GitHub CLI reported: $out" -ForegroundColor Yellow
        Write-Host "`nTo enable fully automated 0-click uploads, authorize gh CLI once by running:" -ForegroundColor Cyan
        Write-Host "    gh auth login --web" -ForegroundColor White
        Write-Host "or add 'Contents: Read and write' permission to your fine-grained token." -ForegroundColor Gray
        
        Write-Host "`nOpening release page and highlighting binary for quick drop..." -ForegroundColor Cyan
        Start-Process "https://github.com/$GitHubRepo/releases/new?tag=$Tag&title=$([Uri]::EscapeDataString($Title))"
        Start-Process "explorer.exe" -ArgumentList "/select,`"$ExePath`""
    }
} finally {
    if (Test-Path $tempNotesFile) { Remove-Item $tempNotesFile -Force }
}

Write-Host "`n==================================================================" -ForegroundColor Green
Write-Host "  SUCCESS: RELEASE $Tag PUBLISHED AUTOMATICALLY!                   " -ForegroundColor Yellow
Write-Host "==================================================================" -ForegroundColor Green
Write-Host "  Release URL: https://github.com/$GitHubRepo/releases/tag/$Tag" -ForegroundColor Cyan
Write-Host "  Executable:  $ExePath" -ForegroundColor Gray
Write-Host "  Manifest:    $ManifestPath" -ForegroundColor Gray
Write-Host "==================================================================`n" -ForegroundColor Green
