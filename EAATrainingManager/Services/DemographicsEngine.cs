using System;
using System.Collections.Generic;
using System.Linq;
using EAATrainingManager.Models;

namespace EAATrainingManager.Services;

public record TraineeMetricSummary(
    int TotalUniqueStudents,
    int EgyptianCount,
    int InternationalCount,
    double InternationalPercentage,
    int Part61Count,
    int Part141Count,
    int EvaluationCount,
    Dictionary<string, int> TopNationalities
);

public class NationalityDistributionItem
{
    public string CountryName { get; set; } = string.Empty;
    public int StudentCount { get; set; }
    public double Percentage { get; set; }
    public string FormattedPercentage => $"{Percentage:F1}%";
    public string FormattedLabel => $"{CountryName}: {StudentCount} متدرب ({FormattedPercentage})";
}

public class DemographicsEngine
{
    private static readonly HashSet<string> EgyptianIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "مصر", "مصرى", "مصري", "مصرية", "مصريه", "ج.م.ع", "جمهورية مصر العربية", "EGY", "EGYPT", "EGYPTIAN"
    };

    /// <summary>
    /// Classifies whether a given nationality string represents an international (foreign/expatriate) student.
    /// Returns true if international, false if local Egyptian.
    /// </summary>
    public static bool ClassifyIfInternational(string? nationality)
    {
        if (string.IsNullOrWhiteSpace(nationality)) return false;
        string cleaned = nationality.Trim();
        return !EgyptianIdentifiers.Contains(cleaned);
    }

    /// <summary>
    /// Standardizes nationality naming variations into clean canonical forms
    /// </summary>
    public static string StandardizeNationality(string? nationality)
    {
        if (string.IsNullOrWhiteSpace(nationality)) return "مصري";
        string cleaned = nationality.Trim();
        if (EgyptianIdentifiers.Contains(cleaned)) return "مصري";

        // Normalize common Arab/African nationalities
        if (cleaned.Contains("كويت")) return "كويتي";
        if (cleaned.Contains("سعود")) return "سعودي";
        if (cleaned.Contains("سودان")) return "سوداني";
        if (cleaned.Contains("ليب")) return "ليبي";
        if (cleaned.Contains("أردن") || cleaned.Contains("اردن")) return "أردني";
        if (cleaned.Contains("عراق")) return "عراقي";
        if (cleaned.Contains("إمارات") || cleaned.Contains("امارات")) return "إماراتي";
        if (cleaned.Contains("يمن")) return "يمني";
        if (cleaned.Contains("بحرين")) return "بحريني";
        if (cleaned.Contains("قطر")) return "قطري";
        if (cleaned.Contains("عمان")) return "عماني";
        if (cleaned.Contains("فلسطين")) return "فلسطيني";
        if (cleaned.Contains("لبنان")) return "لبناني";
        if (cleaned.Contains("سور")) return "سوري";
        if (cleaned.Contains("تشاد")) return "تشادي";
        if (cleaned.Contains("نيجر")) return "نيجيري";

        return cleaned;
    }

    /// <summary>
    /// Computes full demographic and regulatory pathway metrics for a set of students and their associated orders,
    /// optionally filtered by an academic year.
    /// </summary>
    public static TraineeMetricSummary ComputeMetrics(IEnumerable<Student> students, IEnumerable<TrainingOrder> orders, int? targetYear)
    {
        var studentDict = students.ToDictionary(s => s.Id);

        // Filter orders by year if specified
        var filteredOrders = orders
            .Where(o => !targetYear.HasValue || o.AcademicYear == targetYear.Value || o.Year == targetYear.Value)
            .ToList();

        // Unique students present in the filtered orders
        var uniqueStudentIds = filteredOrders.Select(o => o.StudentId).Distinct().ToHashSet();
        var scopedStudents = uniqueStudentIds
            .Where(id => studentDict.ContainsKey(id))
            .Select(id => studentDict[id])
            .ToList();

        int totalStudents = scopedStudents.Count;
        int intlCount = scopedStudents.Count(s => s.IsInternational);
        int egyptianCount = totalStudents - intlCount;
        double intlPct = totalStudents > 0 ? (intlCount * 100.0 / totalStudents) : 0.0;

        int part61 = filteredOrders.Count(o => o.RegulatoryTrack.Equals("Part61", StringComparison.OrdinalIgnoreCase));
        int part141 = filteredOrders.Count(o => o.RegulatoryTrack.Equals("Part141", StringComparison.OrdinalIgnoreCase));
        int eval = filteredOrders.Count(o => o.RegulatoryTrack.Equals("Evaluation", StringComparison.OrdinalIgnoreCase));

        var topNationalities = scopedStudents
            .Where(s => s.IsInternational)
            .GroupBy(s => StandardizeNationality(s.Nationality))
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        return new TraineeMetricSummary(
            TotalUniqueStudents: totalStudents,
            EgyptianCount: egyptianCount,
            InternationalCount: intlCount,
            InternationalPercentage: intlPct,
            Part61Count: part61,
            Part141Count: part141,
            EvaluationCount: eval,
            TopNationalities: topNationalities
        );
    }
}
