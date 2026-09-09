using System;
using EAATrainingManager.Helpers;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

public class TrainingOrder
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string StudentDisplayName { get; set; } = string.Empty;
    public string Nationality { get; set; } = "مصري";
    public string OrderNumber { get; set; } = string.Empty;
    public string ProgramType { get; set; } = "تقييم";
    public string Milestone { get; set; } = "EVALUATION";
    public string RegulationCategory { get; set; } = "تقييم (د)";
    public DateTime? EnrollmentDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int Year { get; set; } = 0;
    public int AcademicYear { get; set; } = 0;
    public string RegulatoryTrack { get; set; } = string.Empty; // 'Part61', 'Part141', 'Evaluation', 'TypeRating'
    public int SequenceNumber { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public string RegulatoryTrackBadgeText => RegulatoryTrack switch
    {
        "Part61" => "حر (61)",
        "Part141" => "معتمد (141)",
        "Evaluation" => "تقييم (د)",
        "TypeRating" => "طراز",
        _ => RegulatoryTrack
    };

    public string RegulatoryTrackBadgeColor => RegulatoryTrack switch
    {
        "Part61" => "#0D6EFD",     // Blue
        "Part141" => "#6F42C1",    // Purple
        "Evaluation" => "#198754", // Green
        _ => "#6C757D"
    };

    public bool IsInternational => DemographicsEngine.ClassifyIfInternational(Nationality);
    public string NationalityBadgeText => IsInternational ? "وافد" : "محلي";
    public string NationalityBadgeColor => IsInternational ? "#FD7E14" : "#198754";

    /// <summary>
    /// Automatic status determination based on completion date:
    /// Blank/null completion date = "قيد التدريب" (Active Trainee in Cockpit)
    /// Valid completion date = "منتهي" (Completed)
    /// </summary>
    public string Status
    {
        get
        {
            if (CompletionDate.HasValue)
                return "منتهي";
            return "قيد التدريب";
        }
    }

    public bool IsActive => !CompletionDate.HasValue;

    public string ToggleButtonText => IsActive ? "إنهاء التدريب ✓" : "إعادة تنشيط ⟲";

    public string StatusBadgeColor => Status switch
    {
        "قيد التدريب" => "#0D6EFD", // Blue (Active)
        "منتهي" => "#198754",        // Green (Completed)
        _ => "#6C757D"
    };

    public string FormattedEnrollmentDate => EnrollmentDate.HasValue
        ? EnrollmentDate.Value.ToString("yyyy/MM/dd")
        : "غير محدد";

    public string FormattedCompletionDate => CompletionDate.HasValue
        ? CompletionDate.Value.ToString("yyyy/MM/dd")
        : "مستمر (قيد التدريب)";

    /// <summary>
    /// Formats order representation with LRM protection for BiDi safety
    /// </summary>
    public string BiDiDisplayName => ArabicTextHelper.WrapAviationBiDi(
        $"{StudentDisplayName} - {ProgramType} [أمر رقم {OrderNumber} لسنة {Year}]");

    public string BiDiProgramType => ArabicTextHelper.WrapAviationBiDi(ProgramType);

    public string BiDiNotes => ArabicTextHelper.WrapAviationBiDi(Notes);
}
