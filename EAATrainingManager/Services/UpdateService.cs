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
    public string CurrentVersion { get; set; } = "2.2.0";
    public string LatestVersion { get; set; } = "2.2.0";
    public double PatchSizeMb { get; set; } = 0.0;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DeltaPatchUrl { get; set; }
}

public class UpdateService
{
    private const string CurrentAppVersion = "2.2.0";
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5) // Fast timeout for airfield/mobile slow internet
        };
    }

    public string GetCurrentVersion() => CurrentAppVersion;

    /// <summary>
    /// Checks for lightweight delta patches (2-5 MB) instead of downloading entire 240 MB runtime.
    /// Gracefully handles 100% offline environments without exceptions.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string? customManifestUrl = null)
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = CurrentAppVersion,
            LatestVersion = CurrentAppVersion
        };

        // Fallback default manifest URL on user's GitHub repository
        string manifestUrl = customManifestUrl ?? "https://raw.githubusercontent.com/michaelmagdy15/EAA-TrainingManager/main/update_manifest.json";

        try
        {
            using var response = await _httpClient.GetAsync(manifestUrl);
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
