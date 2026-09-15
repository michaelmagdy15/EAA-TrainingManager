using System;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

/// <summary>Time-bound operational evidence such as a medical, ELP, or licence.</summary>
public class ComplianceRecord
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentDisplayName { get; set; } = string.Empty;
    public string RecordType { get; set; } = "Medical";
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsVerified { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value.Date < DateTime.Today;
    public bool IsExpiringSoon => ExpiresAt.HasValue && !IsExpired && ExpiresAt.Value.Date <= DateTime.Today.AddDays(30);
    public bool IsBlocking => IsExpired && IsVerified;
    public string RecordTypeText => RecordType switch
    {
        "ELP" => "ICAO ELP",
        "License" => LocalizationService.Instance.Text("رخصة", "Licence"),
        "Passport" => LocalizationService.Instance.Text("جواز سفر", "Passport"),
        _ => LocalizationService.Instance.Text("كشف طبي", "Medical")
    };
    public string StatusText => IsExpired
        ? LocalizationService.Instance.Text("منتهية", "Expired")
        : IsExpiringSoon
            ? LocalizationService.Instance.Text("تنتهي قريباً", "Expiring soon")
            : IsVerified
                ? LocalizationService.Instance.Text("سارية ومعتمدة", "Valid & verified")
                : LocalizationService.Instance.Text("بانتظار التحقق", "Pending verification");
    public string StatusBadgeColor => IsExpired ? "#DC3545" : IsExpiringSoon ? "#FD7E14" : IsVerified ? "#198754" : "#6C757D";
    public string ExpiryText => ExpiresAt.HasValue ? ExpiresAt.Value.ToString("yyyy/MM/dd") : LocalizationService.Instance.Text("غير محدد", "Not set");
}
