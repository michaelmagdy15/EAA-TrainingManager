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
        "Dispatched" => LocalizationService.Instance.Text("تم الترحيل", "Dispatched"),
        "Completed" => LocalizationService.Instance.Text("مكتملة", "Completed"),
        "Cancelled" => LocalizationService.Instance.Text("ملغاة", "Cancelled"),
        _ => LocalizationService.Instance.Text("مجدولة", "Scheduled")
    };
    public string StatusBadgeColor => Status switch
    {
        "Dispatched" => "#0D6EFD",
        "Completed" => "#198754",
        "Cancelled" => "#6C757D",
        _ => "#6F42C1"
    };
}
