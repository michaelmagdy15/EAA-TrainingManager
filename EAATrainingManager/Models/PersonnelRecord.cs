using System;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

public class PersonnelRecord
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Instructor";
    public string LicenseNumber { get; set; } = string.Empty;
    public DateTime? LicenseExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public bool IsCurrent => IsActive && (!LicenseExpiresAt.HasValue || LicenseExpiresAt.Value.Date >= DateTime.Today);
    public string StatusText => IsCurrent ? LocalizationService.Instance.Text("صالح للعمل", "Current") : LocalizationService.Instance.Text("غير صالح للعمل", "Not current");
}
