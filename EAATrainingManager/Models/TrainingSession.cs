using System;
using EAATrainingManager.Helpers;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

/// <summary>
/// A scheduled training activity. Sessions are intentionally separate from
/// training orders so one order can contain many ground, simulator, or flight events.
/// </summary>
public class TrainingSession
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentDisplayName { get; set; } = string.Empty;
    public string RegulatoryTrack { get; set; } = "Part61";
    public string LessonTitle { get; set; } = string.Empty;
    public string InstructorName { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string Status { get; set; } = "Scheduled";
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int DurationMinutes => Math.Max(0, (int)(EndAt - StartAt).TotalMinutes);
    public string TimeRangeText => $"{StartAt:HH:mm} - {EndAt:HH:mm}";
    public string DateText => StartAt.ToString("yyyy/MM/dd");
    public string StudentBiDiName => ArabicTextHelper.WrapAviationBiDi(StudentDisplayName);
    public string LessonBiDiName => ArabicTextHelper.WrapAviationBiDi(LessonTitle);
    public string RegulatoryTrackText => RegulatoryTrack switch
    {
        "Part141" => LocalizationService.Instance.Text("النظام المعتمد 141", "Part 141"),
        "ETP" => LocalizationService.Instance.Text("الخط الجوي ETP", "ETP / Route"),
        "TypeRating" => LocalizationService.Instance.Text("تأهيل طراز", "Type Rating"),
        "Evaluation" => LocalizationService.Instance.Text("تقييم ومعادلة", "Evaluation & Conversion"),
        _ => LocalizationService.Instance.Text("النظام الحر 61", "Part 61")
    };
    public string StatusText => Status switch
    {
        "Confirmed" => LocalizationService.Instance.Text("مؤكدة", "Confirmed"),
        "Released" or "Dispatched" => LocalizationService.Instance.Text("مصرح بالإقلاع", "Released"),
        "Airborne" => LocalizationService.Instance.Text("في الجو", "Airborne"),
        "Landed" => LocalizationService.Instance.Text("تم الهبوط", "Landed"),
        "Completed" => LocalizationService.Instance.Text("مكتملة", "Completed"),
        "Cancelled" => LocalizationService.Instance.Text("ملغاة", "Cancelled"),
        "NoShow" => LocalizationService.Instance.Text("لم يحضر", "No-show"),
        _ => LocalizationService.Instance.Text("مجدولة", "Scheduled")
    };
    public string StatusBadgeColor => Status switch
    {
        "Confirmed" or "Released" or "Dispatched" or "Airborne" => "#0D6EFD",
        "Landed" => "#20A4A8",
        "Completed" => "#198754",
        "Cancelled" or "NoShow" => "#6C757D",
        _ => "#6F42C1"
    };

    public string NextActionText => Status switch
    {
        "Scheduled" => LocalizationService.Instance.Text("تأكيد الحجز", "Confirm booking"),
        "Confirmed" => LocalizationService.Instance.Text("إصدار تصريح", "Release flight"),
        "Released" or "Dispatched" => LocalizationService.Instance.Text("تسجيل الإقلاع", "Mark airborne"),
        "Airborne" => LocalizationService.Instance.Text("تسجيل الهبوط", "Mark landed"),
        "Landed" => LocalizationService.Instance.Text("إتمام سجل الرحلة", "Complete flight record"),
        _ => string.Empty
    };

    public string PostFlightActionText => LocalizationService.Instance.Text("تسجيل ما بعد الرحلة", "Post-flight record");
    public bool CanAdvance => Status is "Scheduled" or "Confirmed" or "Released" or "Dispatched" or "Airborne";
    public bool CanComplete => Status == "Landed";
    public bool CanResolve => Status is "Scheduled" or "Confirmed";
    public bool CanReschedule => Status is "Scheduled" or "Confirmed";
}
