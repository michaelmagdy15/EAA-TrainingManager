using System;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

/// <summary>Result of a stage check, skill test, or other training gate.</summary>
public class TrainingAssessment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentDisplayName { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = "StageCheck";
    public string Title { get; set; } = string.Empty;
    public string ExaminerName { get; set; } = string.Empty;
    public int AttemptNumber { get; set; } = 1;
    public string Result { get; set; } = "Pending";
    public string Deficiencies { get; set; } = string.Empty;
    public string RemedialPlan { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public DateTime AssessedAt { get; set; } = DateTime.Now;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsPassed => Result.Equals("Passed", StringComparison.OrdinalIgnoreCase);
    public string ResultText => Result switch
    {
        "Passed" => LocalizationService.Instance.Text("ناجح", "Passed"),
        "Failed" => LocalizationService.Instance.Text("غير مجتاز", "Not passed"),
        "Conditional" => LocalizationService.Instance.Text("مشروط", "Conditional"),
        _ => LocalizationService.Instance.Text("قيد المراجعة", "Pending")
    };
}
