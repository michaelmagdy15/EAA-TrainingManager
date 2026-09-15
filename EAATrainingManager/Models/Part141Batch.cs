using System;
using System.Collections.Generic;
using System.Linq;
using EAATrainingManager.Helpers;
using EAATrainingManager.Services;

namespace EAATrainingManager.Models;

public class NationalityCountItem
{
    public string Country { get; set; } = "مصري";
    public int Count { get; set; }
    public bool IsInternational => DemographicsEngine.ClassifyIfInternational(Country);
    public string DisplayText => $"{Country}: {Count}";
    public string BadgeColor => IsInternational ? "#FD7E14" : "#0D6EFD";
}

public class Part141Batch
{
    public int Id { get; set; }

    /// <summary>
    /// Batch identifier (e.g., "72", "دفعة 72")
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// Course / division name (اسم الفرقة / البرنامج)
    /// </summary>
    public string ProgramName { get; set; } = "طيران تجاري معتمد (Part 141)";

    /// <summary>
    /// Enrollment date (تاريخ الالتحاق)
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Graduation / Completion date (تاريخ الانتهاء)
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Syllabus logged curriculum hours (عدد الساعات المقررة بالمنهج)
    /// </summary>
    public double SyllabusHours { get; set; } = 190.0;

    /// <summary>
    /// File attachment or document reference list associated with official training decree
    /// (إلحاقات ومرفقات الأمر التدريبي)
    /// </summary>
    public string TrainingOrderAttachments { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
    public int AcademicYear { get; set; } = 2026;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // --- Student Counts & Progress Aggregation ---
    public int TotalStudents { get; set; }
    public int GraduatedStudents { get; set; }
    public int ActiveStudents => Math.Max(0, TotalStudents - GraduatedStudents);

    public double CompletionRate => TotalStudents > 0 
        ? Math.Round((double)GraduatedStudents / TotalStudents * 100, 1) 
        : 0;

    public string ProgressSummaryText => $"{GraduatedStudents} من أصل {TotalStudents} خريج ({CompletionRate}%)";

    // --- Foreign Students & Demographics (الوافدين والجنسيات) ---
    public int LocalStudentsCount { get; set; }
    public int InternationalStudentsCount { get; set; }

    public List<NationalityCountItem> NationalityBreakdown { get; set; } = new();

    /// <summary>
    /// Summary string for nationalities chips
    /// </summary>
    public string NationalitiesSummary => NationalityBreakdown.Count > 0
        ? string.Join(" • ", NationalityBreakdown.Select(n => $"{n.Country} ({n.Count})"))
        : "لا توجد بيانات جنسيات";

    /// <summary>
    /// Student Roster Drilldown: list of students belonging to this batch
    /// </summary>
    public List<Student> StudentRoster { get; set; } = new();

    // --- UI Formatting Helpers ---
    public string FormattedStartDate => StartDate.HasValue 
        ? StartDate.Value.ToString("yyyy/MM/dd") 
        : "غير محدد";

    public string FormattedEndDate => EndDate.HasValue 
        ? EndDate.Value.ToString("yyyy/MM/dd") 
        : "مستمر (قيد التدريب)";

    public bool IsCompleted => EndDate.HasValue && EndDate.Value <= DateTime.Today;

    public string StatusText => IsCompleted ? "دفعة متخرجة" : "دفعة نشطة";
    public string StatusBadgeColor => IsCompleted ? "#198754" : "#6F42C1";

    public List<string> AttachmentsList => string.IsNullOrWhiteSpace(TrainingOrderAttachments)
        ? new List<string>()
        : TrainingOrderAttachments.Split(new[] { ';', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(s => s.Trim())
                                  .Where(s => !string.IsNullOrEmpty(s))
                                  .ToList();

    public int AttachmentsCount => AttachmentsList.Count;
    public string SyllabusHoursText => $"Hours: {SyllabusHours:0.##}";
    public bool HasAttachments => AttachmentsCount > 0;
    public string AttachmentsBadgeText => HasAttachments ? $"{AttachmentsCount} مرفق أمر تدريب" : "بدون مرفقات";

    public string BiDiDisplayName => ArabicTextHelper.WrapAviationBiDi($"{ProgramName} - الدفعة ({BatchId})");
}
