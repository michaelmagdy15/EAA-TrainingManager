using System;
using EAATrainingManager.Helpers;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

/// <summary>
/// Dispatchable aircraft, simulator, or training device used by the operations board.
/// </summary>
public class AircraftResource
{
    public int Id { get; set; }
    public string Registration { get; set; } = string.Empty;
    public string ResourceType { get; set; } = "Aircraft";
    public string AircraftType { get; set; } = string.Empty;
    public string Base { get; set; } = string.Empty;
    public string Status { get; set; } = "Available";
    public double HobbsHours { get; set; }
    public double TachHours { get; set; }
    public double? MaintenanceDueAtHours { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsMaintenanceDue => MaintenanceDueAtHours.HasValue && HobbsHours >= MaintenanceDueAtHours.Value;
    public bool IsDispatchable => Status == "Available" && !IsMaintenanceDue;
    public string DisplayName => string.IsNullOrWhiteSpace(AircraftType) ? Registration : $"{Registration} — {AircraftType}";
    public string BiDiDisplayName => ArabicTextHelper.WrapAviationBiDi(DisplayName);
    public string ResourceTypeText => ResourceType == "Simulator"
        ? LocalizationService.Instance.Text("جهاز محاكاة", "Simulator")
        : LocalizationService.Instance.Text("طائرة", "Aircraft");
    public string StatusText => IsMaintenanceDue ? LocalizationService.Instance.Text("الصيانة مستحقة", "Maintenance due") : Status switch
    {
        "Maintenance" => LocalizationService.Instance.Text("تحت الصيانة", "In maintenance"),
        "Unserviceable" => LocalizationService.Instance.Text("غير صالحة للتشغيل", "Unserviceable"),
        _ => LocalizationService.Instance.Text("متاحة", "Available")
    };
    public string StatusBadgeColor => !IsDispatchable ? "#DC3545" : "#198754";
    public string HoursText => LocalizationService.Instance.Text($"عداد هوبس {HobbsHours:0.0} | عداد تاك {TachHours:0.0}", $"Hobbs {HobbsHours:0.0} | Tach {TachHours:0.0}");
    public string MaintenanceText => MaintenanceDueAtHours.HasValue
        ? LocalizationService.Instance.Text($"الصيانة عند {MaintenanceDueAtHours.Value:0.0} ساعة", $"Due at {MaintenanceDueAtHours.Value:0.0} h")
        : LocalizationService.Instance.Text("لا يوجد حد صيانة مسجل", "No maintenance threshold");
}
