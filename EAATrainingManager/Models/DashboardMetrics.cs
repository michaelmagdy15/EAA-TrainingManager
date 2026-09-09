using System;
using System.Collections.Generic;

namespace EAATrainingManager.Models;

public class ProgramDistributionItem
{
    public string ProgramName { get; set; } = string.Empty;
    public string MilestoneKey { get; set; } = string.Empty;
    public int UniqueStudentsCount { get; set; }
    public int TotalOrdersCount { get; set; }
    public int ActiveCount { get; set; }
    public double Percentage { get; set; }
    public string ColorHex { get; set; } = "#0D6EFD";

    public string DisplayText => $"{ProgramName}: {UniqueStudentsCount} طالب ({TotalOrdersCount} أمر)";

    public string BiDiProgramName => Services.LocalizationService.Instance.IsEnglish ? MilestoneKey switch
    {
        "ATP" => "Airline Transport (ATP)",
        "CPL_IR" => "Commercial & Instruments (CPL/IR)",
        "EVALUATION" => "Equivalency & Evaluations",
        "PPL" => "Private Pilot (PPL)",
        _ => ProgramName
    } : ProgramName;

    public string TraineeUnitLabel => Services.LocalizationService.Instance.IsEnglish ? "Trainees" : "طالب فعلي";
}

public class DashboardMetrics
{
    /// <summary>
    /// Deduplicated headcount of human trainees (إجمالي الطلبة الفعليين)
    /// </summary>
    public int TotalUniqueStudents { get; set; }

    /// <summary>
    /// Total operational training orders issued (إجمالي أوامر التدريب المسجلة)
    /// </summary>
    public int TotalCourseEnrollments { get; set; }

    /// <summary>
    /// Trainees currently active in cockpit flight training (بدون تاريخ نهاية)
    /// </summary>
    public int ActiveTraineesCount { get; set; }

    /// <summary>
    /// Trainees who completed all orders (تاريخ نهاية مسجل)
    /// </summary>
    public int GraduatedTraineesCount { get; set; }

    /// <summary>
    /// Ratio of course orders per unique student (متوسط الأوامر لكل متدرب)
    /// </summary>
    public double OrdersPerStudentRatio => TotalUniqueStudents > 0
        ? Math.Round((double)TotalCourseEnrollments / TotalUniqueStudents, 2)
        : 0.0;

    // Milestone Counts (Unique Students per Milestone)
    public int PPLCount { get; set; }
    public int CPLIRCount { get; set; }
    public int ATPCount { get; set; }
    public int EvaluationCount { get; set; }
    public int TypeRatingCount { get; set; }

    public List<ProgramDistributionItem> ProgramDistribution { get; set; } = new();
    public List<TrainingOrder> RecentOrders { get; set; } = new();
}
