using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace EAATrainingManager.Services;

public class UpdateCheckResult
{
    public bool IsSuccess { get; set; }
    public bool HasUpdate { get; set; }
    public bool IsUpToDate { get; set; }
    public bool IsOffline { get; set; }
    public string CurrentVersion { get; set; } = "2.2.1";
    public string LatestVersion { get; set; } = "2.2.5";
    public double PatchSizeMb { get; set; } = 0.0;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DeltaPatchUrl { get; set; }
}

public class UpdateService
{
    private const string CurrentAppVersion = "2.2.5";
    private const string RepoOwner = "michaelmagdy15";
    private const string RepoName = "EAA-TrainingManager";
    
    // Read-only token for private repo updates (fine-grained: Contents & Releases Read-Only)
    public static string ReadOnlyToken { get; set; } = "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";

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10) // Fast timeout for airfield/mobile slow internet
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "EAA-TrainingManager-Desktop");
    }

    public string GetCurrentVersion() => CurrentAppVersion;

    /// <summary>
    /// Checks for lightweight delta patches (2-5 MB) instead of downloading entire 240 MB runtime.
    /// Supports both public repos and 100% private repos using GitHub API with a read-only token.
    /// Gracefully handles 100% offline environments without exceptions.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string? customManifestUrl = null)
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = CurrentAppVersion,
            LatestVersion = CurrentAppVersion
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "");

            if (!string.IsNullOrWhiteSpace(customManifestUrl))
            {
                request.RequestUri = new Uri(customManifestUrl);
            }
            else if (!string.IsNullOrWhiteSpace(ReadOnlyToken))
            {
                // Private repo: access via GitHub REST API with raw accept header
                request.RequestUri = new Uri($"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/update_manifest.json");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ReadOnlyToken);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
            }
            else
            {
                // Public fallback
                request.RequestUri = new Uri($"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/update_manifest.json");
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                result.IsOffline = true;
                result.Message = LocalizationService.Instance.Text(
                    "المنظومة في وضع عدم الاتصال (100% Offline Mode) - الإصدار الحالي يعمل بكفاءة تامة.",
                    "System is operating in 100% Offline Mode. Current version is fully operational.");
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string latestVer = root.GetProperty("version").GetString() ?? CurrentAppVersion;
            result.LatestVersion = latestVer;
            result.ReleaseNotes = root.TryGetProperty("releaseNotes", out var rn) ? rn.GetString() ?? "" : "";
            result.DeltaPatchUrl = root.TryGetProperty("deltaPatchUrl", out var dp) ? dp.GetString() : null;

            if (root.TryGetProperty("patchSizeBytes", out var ps))
            {
                result.PatchSizeMb = Math.Round(ps.GetInt64() / (1024.0 * 1024.0), 2);
            }
            else
            {
                result.PatchSizeMb = 2.4; // Typical differential patch size
            }

            var current = Version.Parse(CurrentAppVersion);
            var latest = Version.Parse(latestVer);

            if (latest > current)
            {
                result.HasUpdate = true;
                result.IsSuccess = true;
                result.Message = LocalizationService.Instance.Text(
                    $"يتوفر تحديث خفيف جديد v{latestVer} (حجم التحديث التفاضلي: ~{result.PatchSizeMb} ميجابايت فقط).",
                    $"Lightweight delta update v{latestVer} available (Patch size: ~{result.PatchSizeMb} MB only).");
            }
            else
            {
                result.IsUpToDate = true;
                result.IsSuccess = true;
                result.Message = LocalizationService.Instance.Text(
                    $"المنظومة محدثة بالكامل لأحدث إصدار رسمي معتمد (v{CurrentAppVersion}).",
                    $"System is completely up to date with official release (v{CurrentAppVersion}).");
            }
        }
        catch
        {
            // Fully offline - no crash, no freeze
            result.IsOffline = true;
            result.Message = LocalizationService.Instance.Text(
                $"تعذر الوصول لشبكة التحديثات (المنظومة تعمل محلياً 100% بدون إنترنت). الإصدار الحالي v{CurrentAppVersion} مستقر.",
                $"Updates server unreachable (System operating 100% offline). Current version v{CurrentAppVersion} is stable.");
        }

        return result;
    }

    /// <summary>
    /// Downloads the updated standalone executable in chunks with progress reporting,
    /// writes it to a temporary file, and triggers a seamless background restart updater.
    /// Handles private GitHub releases, non-redirected bearer tokens for Azure Blob storage,
    /// and ensures zero admin elevation requirements.
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync(
        string latestVersion,
        Action<long, long, int>? progressCallback = null,
        Action<string>? statusCallback = null)
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string currentExePath = Environment.ProcessPath 
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName 
                ?? Path.Combine(appDir, "EAATrainingManager.exe");
            int currentPid = Environment.ProcessId;
            bool isModular = File.Exists(Path.Combine(appDir, "EAATrainingManager.dll"));

            statusCallback?.Invoke(LocalizationService.Instance.Text(
                "جارٍ تحديد موقع حزمة التحديث من المستودع السحابي...",
                "Locating update package in cloud release..."));

            string downloadDirectUrl = string.Empty;
            long expectedSize = 0;
            bool isDeltaZip = false;

            if (!string.IsNullOrWhiteSpace(ReadOnlyToken))
            {
                // Query release metadata from private repo
                using var relReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/v{latestVersion}");
                relReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ReadOnlyToken);
                relReq.Headers.UserAgent.ParseAdd("EAA-TrainingManager-Desktop");
                relReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var relResp = await _httpClient.SendAsync(relReq);
                if (!relResp.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to retrieve release metadata: HTTP {(int)relResp.StatusCode}");
                }

                var relJson = await relResp.Content.ReadAsStringAsync();
                using var relDoc = JsonDocument.Parse(relJson);
                if (relDoc.RootElement.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                {
                    string chosenAssetApiUrl = string.Empty;

                    // 1. If modular installation, prioritize lightweight delta zip patch (0.6 - 2 MB)
                    if (isModular)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.GetProperty("name").GetString() ?? "";
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                (name.Contains("Delta", StringComparison.OrdinalIgnoreCase) || name.Contains("patch", StringComparison.OrdinalIgnoreCase)))
                            {
                                chosenAssetApiUrl = asset.GetProperty("url").GetString()!;
                                expectedSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                                isDeltaZip = true;
                                break;
                            }
                        }
                    }

                    // 2. Fallback to standalone .exe if not modular or delta zip not found
                    if (string.IsNullOrWhiteSpace(chosenAssetApiUrl))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string name = asset.GetProperty("name").GetString() ?? "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAssetApiUrl = asset.GetProperty("url").GetString()!;
                                expectedSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                                isDeltaZip = false;
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(chosenAssetApiUrl))
                    {
                        // Request direct Azure/S3 download link via 302 Found redirect without following
                        var noRedirectHandler = new HttpClientHandler { AllowAutoRedirect = false };
                        using var noRedirectClient = new HttpClient(noRedirectHandler) { Timeout = TimeSpan.FromSeconds(30) };
                        
                        using var assetReq = new HttpRequestMessage(HttpMethod.Get, chosenAssetApiUrl);
                        assetReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ReadOnlyToken);
                        assetReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));
                        assetReq.Headers.UserAgent.ParseAdd("EAA-TrainingManager-Desktop");

                        using var assetResp = await noRedirectClient.SendAsync(assetReq);
                        if (assetResp.StatusCode == System.Net.HttpStatusCode.Found ||
                            assetResp.StatusCode == System.Net.HttpStatusCode.Moved ||
                            assetResp.StatusCode == System.Net.HttpStatusCode.SeeOther)
                        {
                            downloadDirectUrl = assetResp.Headers.Location?.ToString() ?? string.Empty;
                        }
                        else if (assetResp.IsSuccessStatusCode)
                        {
                            downloadDirectUrl = chosenAssetApiUrl;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadDirectUrl))
            {
                // Fallback to public release asset URL
                downloadDirectUrl = isModular 
                    ? $"https://github.com/{RepoOwner}/{RepoName}/releases/download/v{latestVersion}/EAA_Delta_Patch_v{latestVersion}.zip"
                    : $"https://github.com/{RepoOwner}/{RepoName}/releases/download/v{latestVersion}/EAATrainingManager.exe";
            }

            statusCallback?.Invoke(LocalizationService.Instance.Text(
                isDeltaZip ? "جارٍ بدء تنزيل التحديث التفاضلي الخفيف (Delta Patch)..." : "جارٍ بدء تنزيل حزمة التحديث الكاملة...",
                isDeltaZip ? "Initiating lightweight delta patch download..." : "Initiating package download..."));

            string tempFilePath = Path.Combine(Path.GetTempPath(), isDeltaZip 
                ? $"EAATrainingManager_Delta_v{latestVersion}.zip" 
                : $"EAATrainingManager_Update_v{latestVersion}.exe");

            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }

            // Download binary stream cleanly without GitHub authorization header (compatible with Azure Blob / S3 SAS)
            using (var downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
            {
                downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("EAA-TrainingManager-Desktop");

                using var response = await downloadClient.GetAsync(downloadDirectUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? expectedSize;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    int percentage = totalBytes > 0 ? (int)((totalBytesRead * 100) / totalBytes) : 0;
                    progressCallback?.Invoke(totalBytesRead, totalBytes, percentage);

                    double readMb = Math.Round(totalBytesRead / (1024.0 * 1024.0), 2);
                    double totalMb = totalBytes > 0 ? Math.Round(totalBytes / (1024.0 * 1024.0), 2) : readMb;

                    statusCallback?.Invoke(LocalizationService.Instance.Text(
                        $"جارٍ تحميل حزمة التحديث: {readMb} ميجابايت من {totalMb} ميجابايت ({percentage}%)",
                        $"Downloading update package: {readMb} MB of {totalMb} MB ({percentage}%)"));
                }
            }

            // Verify downloaded binary exists and has non-trivial size
            var downloadedInfo = new FileInfo(tempFilePath);
            long minRequiredBytes = isDeltaZip ? (50 * 1024) : (5 * 1024 * 1024);
            if (!downloadedInfo.Exists || downloadedInfo.Length < minRequiredBytes)
            {
                throw new Exception("Downloaded update file is corrupt or incomplete.");
            }

            statusCallback?.Invoke(LocalizationService.Instance.Text(
                "اكتمل التنزيل بنجاح! جارٍ تثبيت الإصدار وإعادة تشغيل المنظومة تلقائياً...",
                "Download completed! Applying update and relaunching application..."));

            // Generate self-cleaning updater batch script
            string updaterBatchPath = Path.Combine(Path.GetTempPath(), "eaa_updater.bat");
            string scriptContent;

            if (isDeltaZip)
            {
                // Extract lightweight delta patch over application folder
                scriptContent = $@"@echo off
timeout /t 1 /nobreak >nul
:waitloop
tasklist /fi ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
tar -xf ""{tempFilePath}"" -C ""{appDir}"" 2>nul || powershell -NoProfile -Command ""Expand-Archive -Path '{tempFilePath}' -DestinationPath '{appDir}' -Force""
start """" ""{currentExePath}""
del ""{tempFilePath}"" 2>nul
del ""%~f0"" 2>nul
";
            }
            else
            {
                // Overwrite single executable
                scriptContent = $@"@echo off
timeout /t 1 /nobreak >nul
:waitloop
tasklist /fi ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)
copy /y ""{tempFilePath}"" ""{currentExePath}"" >nul
start """" ""{currentExePath}""
del ""{tempFilePath}"" 2>nul
del ""%~f0"" 2>nul
";
            }

            File.WriteAllText(updaterBatchPath, scriptContent);

            // Launch background updater script
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{updaterBatchPath}\"",
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);

            // Gracefully terminate current process so updater script can overwrite it
            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            statusCallback?.Invoke(LocalizationService.Instance.Text(
                $"فشل تنزيل التحديث: {ex.Message}",
                $"Update installation failed: {ex.Message}"));
            return false;
        }
    }
}
