using System;
using EAATrainingManager.Helpers;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

/// <summary>Immutable operational record for a completed flight, simulator sortie, or ground lesson.</summary>
public class FlightRecord
{
    public int Id { get; set; }
    public int? TrainingSessionId { get; set; }
    public int StudentId { get; set; }
    public string StudentDisplayName { get; set; } = string.Empty;
    public string ActivityType { get; set; } = "Dual";
    public string ResourceName { get; set; } = string.Empty;
    public string InstructorName { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public double HobbsStart { get; set; }
    public double HobbsEnd { get; set; }
    public int Landings { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public double DurationHours => Math.Max(0, (EndAt - StartAt).TotalHours);
    public double HobbsDelta => Math.Max(0, HobbsEnd - HobbsStart);
    public string DurationText => LocalizationService.Instance.Text($"{DurationHours:0.00} ساعة", $"{DurationHours:0.00} h");
    public string ActivityTypeText => ActivityType switch
    {
        "Solo" => LocalizationService.Instance.Text("طيران فردي", "Solo"),
        "PIC" => "PIC",
        "Simulator" => LocalizationService.Instance.Text("محاكي", "Simulator"),
        "Ground" => LocalizationService.Instance.Text("تدريب أرضي", "Ground"),
        _ => LocalizationService.Instance.Text("طيران مزدوج", "Dual")
    };
    public string StudentBiDiName => ArabicTextHelper.WrapAviationBiDi(StudentDisplayName);
    public string TimeText => $"{StartAt:yyyy/MM/dd HH:mm} - {EndAt:HH:mm}";
}
