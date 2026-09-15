using System;
using System.Collections.Generic;
using EAATrainingManager.Helpers;

namespace EAATrainingManager.Models;

/// <summary>
/// Dedicated operational model for ETP / Route Flying (الخط جوي)
/// Operates independently from Part 141 approved batches.
/// </summary>
public class ETPBatch
{
    public int Id { get; set; }

    /// <summary>
    /// Flight route identifier / batch code (رمز الخط أو كود التشغيل)
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// Flight sector / route name (المسار الجوي أو قطاع الخط)
    /// </summary>
    public string RouteName { get; set; } = "تدريب خطوط جوية تجارية (ETP / Route Line Training)";

    /// <summary>
    /// Airline carrier / company partner (شركة الطيران المشغلة)
    /// </summary>
    public string AirlineCompany { get; set; } = string.Empty;

    /// <summary>
    /// Start date of line training operations
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Completion / check-ride date
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Total operational flight hours on route (ساعات الطيران الفعلي بالخط)
    /// </summary>
    public double FlightHours { get; set; } = 50.0;

    public string Notes { get; set; } = string.Empty;
    public int AcademicYear { get; set; } = 2026;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Aggregations
    public int TotalPilots { get; set; }
    public int CompletedPilots { get; set; }
    public int ActivePilots => Math.Max(0, TotalPilots - CompletedPilots);

    public List<Student> PilotsRoster { get; set; } = new();
    public List<TrainingOrder> Orders { get; set; } = new();

    public string FormattedStartDate => StartDate.HasValue ? StartDate.Value.ToString("yyyy/MM/dd") : "غير محدد";
    public string FormattedEndDate => EndDate.HasValue ? EndDate.Value.ToString("yyyy/MM/dd") : "مستمر بالخط الجوي";

    public bool IsCompleted => EndDate.HasValue && EndDate.Value <= DateTime.Today;
    public string StatusText => IsCompleted ? "مكتمل" : "نشط بالخط الجوي";
    public string StatusBadgeColor => IsCompleted ? "#198754" : "#0D6EFD";
    public string FlightHoursText => $"Hours: {FlightHours:0.##}";
    public string TotalPilotsText => $"Pilots: {TotalPilots}";

    public string BiDiDisplayName => ArabicTextHelper.WrapAviationBiDi(
        string.IsNullOrWhiteSpace(AirlineCompany) 
            ? $"{RouteName} - {BatchId}" 
            : $"{AirlineCompany} | {RouteName} ({BatchId})");
}
