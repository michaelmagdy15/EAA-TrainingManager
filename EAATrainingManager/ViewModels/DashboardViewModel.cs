using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;
    private readonly ExcelSyncService _excelService;

    [ObservableProperty]
    private int _totalUniqueStudents;

    [ObservableProperty]
    private int _totalCourseEnrollments;

    [ObservableProperty]
    private int _activeTraineesCount;

    [ObservableProperty]
    private int _graduatedTraineesCount;

    [ObservableProperty]
    private double _ordersPerStudentRatio;

    // Demographic & Regulatory Track Metrics
    [ObservableProperty]
    private int _internationalStudentsCount;

    [ObservableProperty]
    private int _egyptianStudentsCount;

    [ObservableProperty]
    private string _internationalPercentageText = "0% من الإجمالي";

    [ObservableProperty]
    private int _part61Count;

    [ObservableProperty]
    private int _part141Count;

    [ObservableProperty]
    private int _evaluationCount;

    // Temporal Scoping Filter
    [ObservableProperty]
    private int? _selectedAcademicYear = null; // null = All-Time

    [ObservableProperty]
    private string _selectedYearText = "جميع السنوات";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<ProgramDistributionItem> ProgramDistribution { get; } = new();
    public ObservableCollection<TrainingOrder> RecentOrders { get; } = new();
    public ObservableCollection<NationalityDistributionItem> TopNationalities { get; } = new();

    public DashboardViewModel(DatabaseService dbService, ExcelSyncService excelService)
    {
        _dbService = dbService;
        _excelService = excelService;
    }

    [RelayCommand]
    public async Task SetAcademicYearAsync(string? yearTag)
    {
        if (string.IsNullOrWhiteSpace(yearTag) || yearTag.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            SelectedAcademicYear = null;
            SelectedYearText = "جميع السنوات";
        }
        else if (int.TryParse(yearTag, out int yr))
        {
            SelectedAcademicYear = yr;
            SelectedYearText = yr.ToString();
        }

        await LoadMetricsAsync();
    }

    [RelayCommand]
    public async Task LoadMetricsAsync()
    {
        IsLoading = true;
        StatusMessage = $"جاري تحميل المؤشرات والبيانات التدريبية ({SelectedYearText})...";

        try
        {
            await _dbService.InitializeAsync();
            var metrics = await _dbService.GetDashboardMetricsAsync(SelectedAcademicYear);
            var demoSummary = await _dbService.GetDemographicsSummaryAsync(SelectedAcademicYear);

            TotalUniqueStudents = metrics.TotalUniqueStudents;
            TotalCourseEnrollments = metrics.TotalCourseEnrollments;
            ActiveTraineesCount = metrics.ActiveTraineesCount;
            GraduatedTraineesCount = metrics.GraduatedTraineesCount;
            OrdersPerStudentRatio = metrics.OrdersPerStudentRatio;

            // Update Demographics & Stream Counts
            InternationalStudentsCount = demoSummary.InternationalCount;
            EgyptianStudentsCount = demoSummary.EgyptianCount;
            InternationalPercentageText = $"{demoSummary.InternationalPercentage:F1}% من إجمالي الأكاديمية";
            Part61Count = demoSummary.Part61Count;
            Part141Count = demoSummary.Part141Count;
            EvaluationCount = demoSummary.EvaluationCount;

            // Update Top Nationalities Breakdown
            TopNationalities.Clear();
            int totalIntl = demoSummary.InternationalCount;
            foreach (var kvp in demoSummary.TopNationalities)
            {
                double pct = totalIntl > 0 ? (kvp.Value * 100.0 / totalIntl) : 0;
                TopNationalities.Add(new NationalityDistributionItem
                {
                    CountryName = kvp.Key,
                    StudentCount = kvp.Value,
                    Percentage = pct
                });
            }

            ProgramDistribution.Clear();
            foreach (var item in metrics.ProgramDistribution)
            {
                ProgramDistribution.Add(item);
            }

            RecentOrders.Clear();
            foreach (var order in metrics.RecentOrders)
            {
                RecentOrders.Add(order);
            }

            StatusMessage = TotalUniqueStudents > 0
                ? $"تم تحديث المؤشرات بنجاح ({SelectedYearText}: {TotalUniqueStudents} طالب فعلي - {InternationalStudentsCount} وافد)"
                : "قاعدة البيانات جاهزة. يمكنك بدء استيراد سجل أوامر التدريب عبر زر المزامنة السريعة.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ أثناء تحميل البيانات: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ExportInternationalRosterAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "جاري إعداد وتصدير كشف الطلبة الوافدين للإدارة المركزية للطيران المدني...";

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string fileName = SelectedAcademicYear.HasValue
                ? $"كشف_الطلبة_الوافدين_{SelectedAcademicYear.Value}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                : $"كشف_الطلبة_الوافدين_كافة_السنوات_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            string targetPath = Path.Combine(desktopPath, fileName);

            await _excelService.ExportInternationalStudentsRosterAsync(targetPath, SelectedAcademicYear);
            StatusMessage = $"تم تصدير كشف الوافدين بنجاح إلى سطح المكتب: {fileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"فشل تصدير كشف الوافدين: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ExportMinistryReportAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "جاري إعداد السجل الوزاري المعتمد...";

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string fileName = $"سجل_أوامر_التدريب_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            string targetPath = Path.Combine(desktopPath, fileName);

            await _excelService.ExportOfficialMinistryReportAsync(targetPath, null, SelectedAcademicYear);
            StatusMessage = $"تم تصدير السجل المعتمد بنجاح: {fileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"فشل التصدير: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task QuickImportDefaultExcelAsync()
    {
        string defaultPath = @"C:\Users\Mi5a\EAA System\2اوامر التدريب.xlsx";
        if (!File.Exists(defaultPath))
        {
            StatusMessage = "لم يتم العثور على ملف الإكسيل الافتراضي في المجلد.";
            return;
        }

        IsLoading = true;
        StatusMessage = "جاري استيراد وتفكيك سجلات الإكسيل وحساب الرؤوس الفعلية وتصنيف الجنسيات...";

        try
        {
            var result = await _excelService.ImportFromWorkbookAsync(defaultPath);
            await LoadMetricsAsync();
            StatusMessage = $"تمت المزامنة بنجاح! تم حصر {result.UniqueStudentsCount} طالب فعلي من أصل {result.OrdersImported} أمر تدريب.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"فشل الاستيراد: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
