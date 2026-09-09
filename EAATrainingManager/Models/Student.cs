using System;
using System.Collections.Generic;
using EAATrainingManager.Helpers;

namespace EAATrainingManager.Models;

public class Student
{
    public int Id { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Nationality { get; set; } = "مصري";
    public bool IsInternational { get; set; }
    public string NationalityBadgeText => IsInternational ? "وافد" : "محلي";
    public string NationalityBadgeColor => IsInternational ? "#FD7E14" : "#198754";
    public string NationalId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // Derived Status & Metrics
    public int TotalOrdersCount { get; set; }
    public int ActiveOrdersCount { get; set; }
    public int CompletedOrdersCount { get; set; }

    /// <summary>
    /// Overall pipeline status:
    /// "قيد التدريب" if currently active in at least one order without completion date,
    /// "خريج" if all orders completed,
    /// "مسجل جديد" if no orders yet.
    /// </summary>
    public string OverallStatus
    {
        get
        {
            if (ActiveOrdersCount > 0)
                return "قيد التدريب";
            if (TotalOrdersCount > 0 && CompletedOrdersCount == TotalOrdersCount)
                return "خريج";
            if (TotalOrdersCount > 0)
                return "مكتمل جزئياً";
            return "مسجل جديد";
        }
    }

    public string StatusBadgeColor => OverallStatus switch
    {
        "قيد التدريب" => "#0D6EFD", // Accent Blue
        "خريج" => "#198754",        // Success Green
        "مكتمل جزئياً" => "#FD7E14", // Warning Orange
        _ => "#6C757D"              // Neutral Gray
    };

    // Milestone badges
    public bool HasPPL { get; set; }
    public bool HasCPLIR { get; set; }
    public bool HasATP { get; set; }
    public bool HasEvaluation { get; set; }

    public string MilestonesSummary
    {
        get
        {
            var list = new List<string>();
            if (HasPPL) list.Add("PPL");
            if (HasCPLIR) list.Add("CPL/IR");
            if (HasATP) list.Add("ATP");
            if (HasEvaluation) list.Add("تقييم");
            if (list.Count == 0) return "لا توجد مراحل مسجلة";
            return ArabicTextHelper.WrapAviationBiDi(string.Join(" 🡸 ", list));
        }
    }

    public List<TrainingOrder> Orders { get; set; } = new();

    public string DisplayNameWithBiDi => ArabicTextHelper.WrapAviationBiDi(DisplayName);
}
