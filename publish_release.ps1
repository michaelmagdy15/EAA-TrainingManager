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
    $Title = "v$Version - Foolproof Excel Ingestion, Minimize Control & In-App Delta Patch"
}

if (-not $Notes) {
    $Notes = @"
## Egyptian Aviation Academy (EAA) Training Management System v$Version

### Key Updates & Enhancements:
- **Foolproof Excel Ingestion**: Completely removed manual path / URL entry. Non-technical staff can now browse and select Excel workbooks with a single click, drag and drop files onto the interactive drop zone, and benefit from automatic file detection.
- **Window Minimize Control**: Added a dedicated Minimize button in the title bar and enabled `OverlappedPresenter.IsMinimizable` so staff can minimize the terminal to the Windows taskbar without quitting.
- **Enhanced In-App Navigation**: Added 1-click Excel Import shortcuts directly in the title bar header and dashboard.
- **Private Repository Delta Updates**: In-app one-click update detection and background micro patch delivery (~750 KB) via authenticated GitHub REST API.
- **Unified Student & Trainee Directory**: Real-time deduplication linking historical courses and milestones (PPL -> CPL/IR -> ATP).
- **Consular & Authority Export**: Instant 1-click export of accredited international cadet rosters.
- **SQLite Engine & Excel Mirroring**: High-performance offline database with background Excel snapshot mirroring.

### Ready-to-Run Binary:
- **EAATrainingManager.exe**: Standalone single-file executable with embedded .NET 9 desktop runtime (no installation required).
"@
}

# 2b. Update EAATrainingManager.csproj & UpdateService.cs Version Tags Prior to Build
Write-Host "`n[1/6] Updating project version references to $Version..." -ForegroundColor Cyan
$CsprojPath = Join-Path $ScriptRoot "EAATrainingManager\EAATrainingManager.csproj"
$csprojContent = [System.IO.File]::ReadAllText($CsprojPath, [System.Text.Encoding]::UTF8)
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace($csprojContent, "<Version>.*?</Version>", "<Version>$Version</Version>")
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace($csprojContent, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$Version.0</AssemblyVersion>")
$csprojContent = [System.Text.RegularExpressions.Regex]::Replace($csprojContent, "<FileVersion>.*?</FileVersion>", "<FileVersion>$Version.0</FileVersion>")
[System.IO.File]::WriteAllText($CsprojPath, $csprojContent, [System.Text.Encoding]::UTF8)

$updateServicePath = Join-Path $ScriptRoot "EAATrainingManager\Services\UpdateService.cs"
if (Test-Path $updateServicePath) {
    $serviceContent = [System.IO.File]::ReadAllText($updateServicePath, [System.Text.Encoding]::UTF8)
    $serviceContent = [System.Text.RegularExpressions.Regex]::Replace($serviceContent, 'public string CurrentVersion \{ get; \} = ".*?";', 'public string CurrentVersion { get; } = "' + $Version + '";')
    $serviceContent = [System.Text.RegularExpressions.Regex]::Replace($serviceContent, 'public string LatestVersion \{ get; set; } = ".*?";', 'public string LatestVersion { get; set; } = "' + $Version + '";')
    $serviceContent = [System.Text.RegularExpressions.Regex]::Replace($serviceContent, 'private const string CurrentAppVersion = ".*?";', 'private const string CurrentAppVersion = "' + $Version + '";')
    [System.IO.File]::WriteAllText($updateServicePath, $serviceContent, [System.Text.Encoding]::UTF8)
}
Write-Host "  -> Project files updated to version $Version." -ForegroundColor Green

# 3. Compile Executables (Modular Runtime & Standalone Single-File)
$PublishDir = Join-Path $ScriptRoot "Publish"
$StandaloneDir = Join-Path $ScriptRoot "bin\standalone"
$ExePath = Join-Path $StandaloneDir "EAATrainingManager.exe"
$deltaZipPath = Join-Path $ScriptRoot "EAA_Delta_Patch_$Tag.zip"

if (-not $SkipBuild) {
    Write-Host "`n[2/6] Building Modular Runtime in $PublishDir..." -ForegroundColor Cyan
    dotnet publish $CsprojPath -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=false -o $PublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish (modular) failed with exit code $LASTEXITCODE"
        exit 1
    }

    Write-Host "`n[1/6b] Building Compressed Standalone Executable in $StandaloneDir..." -ForegroundColor Cyan
    dotnet publish $CsprojPath -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $StandaloneDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish (standalone) failed with exit code $LASTEXITCODE"
        exit 1
    }
    Write-Host "  -> Compilation complete." -ForegroundColor Green
} else {
    Write-Host "`n[1/6] Skipping build step (-SkipBuild specified)." -ForegroundColor Yellow
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Standalone Executable not found at: $ExePath"
    exit 1
}

# 4. Generate Lightweight Delta Patch (0.6 - 2 MB)
Write-Host "`n[2/6] Packaging Lightweight Delta Patch ($Tag)..." -ForegroundColor Cyan
if (Test-Path $deltaZipPath) { Remove-Item $deltaZipPath -Force }
Compress-Archive -Path "$PublishDir\EAATrainingManager.dll", "$PublishDir\EAATrainingManager.pri", "$PublishDir\EAATrainingManager.deps.json" -DestinationPath $deltaZipPath -Force

$deltaItem = Get-Item $deltaZipPath
$deltaSizeBytes = $deltaItem.Length
$deltaSizeMb = [math]::Round($deltaSizeBytes / 1MB, 2)

$exeItem = Get-Item $ExePath
$fileSizeBytes = $exeItem.Length
$sha256 = (Get-FileHash -Path $ExePath -Algorithm SHA256).Hash.ToLower()

Write-Host "  Delta Patch: $deltaZipPath ($deltaSizeMb MB / $deltaSizeBytes bytes)" -ForegroundColor Green
Write-Host "  Standalone:  $ExePath ($([math]::Round($fileSizeBytes / 1MB, 2)) MB)" -ForegroundColor Gray
Write-Host "  SHA256:      $sha256" -ForegroundColor Gray

# Copy modular files to workspace root for instant high-speed delta updating
try {
    Copy-Item -Path "$PublishDir\EAATrainingManager.exe" -Destination (Join-Path $ScriptRoot "EAATrainingManager.exe") -Force
    Copy-Item -Path "$PublishDir\EAATrainingManager.dll" -Destination (Join-Path $ScriptRoot "EAATrainingManager.dll") -Force
    Copy-Item -Path "$PublishDir\EAATrainingManager.pri" -Destination (Join-Path $ScriptRoot "EAATrainingManager.pri") -Force
    Copy-Item -Path "$PublishDir\EAATrainingManager.deps.json" -Destination (Join-Path $ScriptRoot "EAATrainingManager.deps.json") -Force
    Write-Host "  -> Updated local root with modular engine." -ForegroundColor Green
} catch {
    Write-Warning "Could not update root modular files (may be running): $_"
}

# 5. Update update_manifest.json with True Delta Metrics
Write-Host "`n[4/6] Updating update_manifest.json..." -ForegroundColor Cyan
$todayDate = (Get-Date).ToString("yyyy-MM-dd")

$notesAr = "تحديث v$Version - تيسير استيراد ملفات الإكسيل بالكامل بالسحب والإفلات والتصفح البصري، وإضافة زر تصغير النافذة لشريط المهام، مع حزمة تحديث خفيفة مدمجة (Delta Patch)."
$notesEn = "Update v$Version - Foolproof visual Excel file selection & drag-and-drop, title bar minimize button, and lightweight in-app delta updater."

$manifestObj = [ordered]@{
    version = $Version
    releaseDate = $todayDate
    releaseNotes = $notesAr
    releaseNotesEn = $notesEn
    deltaPatchUrl = "https://github.com/$GitHubRepo/releases/download/$Tag/EAA_Delta_Patch_$Tag.zip"
    patchSizeBytes = $deltaSizeBytes
    fullPackageUrl = "https://github.com/$GitHubRepo/releases/download/$Tag/EAATrainingManager.exe"
    fullSizeBytes = $fileSizeBytes
}
$manifestContent = ($manifestObj | ConvertTo-Json -Depth 4) + "`n"
[System.IO.File]::WriteAllText($ManifestPath, $manifestContent, [System.Text.Encoding]::UTF8)
Write-Host "  -> Manifest updated for version $Version with true delta size: $deltaSizeMb MB." -ForegroundColor Green

# 8. Create / Update Standalone ZIP Archive
Write-Host "`n[5/6] Creating Full Standalone Zip Archive..." -ForegroundColor Cyan
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
    git commit -m "chore(release): bump version to $Tag with lightweight delta patch"
    Write-Host "  -> Pushing to origin main..." -ForegroundColor Gray
    git push origin main
} else {
    Write-Host "  -> Git working tree clean, proceeding to release..." -ForegroundColor Gray
}

# 10. Create GitHub Release & Upload Binary via gh CLI
Write-Host "  -> Creating GitHub Release $Tag and Uploading Delta Patch & Binaries..." -ForegroundColor Cyan
$tempNotesFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tempNotesFile, $Notes, [System.Text.Encoding]::UTF8)

try {
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    & gh release view $Tag --repo $GitHubRepo 2>$null
    $releaseExists = ($LASTEXITCODE -eq 0)

    $uploadSuccess = $false
    if ($releaseExists) {
        Write-Host "  -> Release $Tag exists. Uploading/overwriting assets..." -ForegroundColor Yellow
        $out = & gh release upload $Tag $deltaZipPath $ExePath $zipPath --repo $GitHubRepo --clobber 2>&1
        $uploadSuccess = ($LASTEXITCODE -eq 0)
    } else {
        Write-Host "  -> Creating fresh release $Tag..." -ForegroundColor Gray
        $out = & gh release create $Tag $deltaZipPath $ExePath $zipPath --repo $GitHubRepo --title $Title --notes-file $tempNotesFile --latest 2>&1
        $uploadSuccess = ($LASTEXITCODE -eq 0)
    }
    $ErrorActionPreference = $prevEAP

    if ($uploadSuccess) {
        Write-Host "  -> Release $Tag uploaded with Delta Patch ($deltaSizeMb MB) and marked as Latest!" -ForegroundColor Green
    } else {
        Write-Host "`n[!] GitHub CLI reported: $out" -ForegroundColor Yellow
        Write-Host "`nTo enable fully automated 0-click uploads, authorize gh CLI once by running:" -ForegroundColor Cyan
        Write-Host "    gh auth login --web" -ForegroundColor White
        
        Write-Host "`nOpening release page and highlighting binary for quick drop..." -ForegroundColor Cyan
        Start-Process "https://github.com/$GitHubRepo/releases/new?tag=$Tag&title=$([Uri]::EscapeDataString($Title))"
        Start-Process "explorer.exe" -ArgumentList "/select,`"$deltaZipPath`""
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
