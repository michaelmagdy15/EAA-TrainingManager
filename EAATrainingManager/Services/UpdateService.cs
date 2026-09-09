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
    public string LatestVersion { get; set; } = "2.2.2";
    public double PatchSizeMb { get; set; } = 0.0;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DeltaPatchUrl { get; set; }
}

public class UpdateService
{
    private const string CurrentAppVersion = "2.2.1";
    private const string RepoOwner = "michaelmagdy15";
    private const string RepoName = "EAA-TrainingManager";
    
    // Read-only token for private repo updates (fine-grained: Contents & Releases Read-Only)
    public static string ReadOnlyToken { get; set; } = "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";

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5) // Fast timeout for airfield/mobile slow internet
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
                    "المنظومة في وضع عدم الاتصال (100% Offline Mode) - الإصدار الحالي v2.2.0 يعمل بكفاءة تامة.",
                    "System is operating in 100% Offline Mode. Current version v2.2.0 is fully operational.");
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
                "تعذر الوصول لشبكة التحديثات (المنظومة تعمل محلياً 100% بدون إنترنت). الإصدار الحالي v2.2.0 مستقر.",
                "Updates server unreachable (System operating 100% offline). Current version v2.2.0 is stable.");
        }

        return result;
    }
}
