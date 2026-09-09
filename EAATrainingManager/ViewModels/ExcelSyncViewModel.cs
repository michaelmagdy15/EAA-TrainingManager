using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAATrainingManager.Services;

namespace EAATrainingManager.ViewModels;

public partial class ExcelSyncViewModel : ObservableObject
{
    private readonly ExcelSyncService _excelService;
    private readonly DatabaseService _dbService;

    [ObservableProperty]
    private string _excelFilePath = @"C:\Users\Mi5a\EAA System\2اوامر التدريب.xlsx";

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncProgressText = "جاهز للمزامنة";

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    public ObservableCollection<string> SyncLogs { get; } = new();

    public ExcelSyncViewModel(ExcelSyncService excelService, DatabaseService dbService)
    {
        _excelService = excelService;
        _dbService = dbService;
    }

    [RelayCommand]
    public async Task StartImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExcelFilePath) || !File.Exists(ExcelFilePath))
        {
            SyncProgressText = "الملف المحدد غير موجود!";
            return;
        }

        IsSyncing = true;
        SyncLogs.Clear();
        SyncProgressText = "جاري بدء المزامنة وتفكيك السجلات...";

        try
        {
            var result = await _excelService.ImportFromWorkbookAsync(ExcelFilePath, (cur, total, msg) =>
            {
                SyncProgressText = msg;
            });

            foreach (var log in result.LogMessages)
            {
                SyncLogs.Add(log);
            }

            SyncProgressText = $"اكتملت المزامنة: {result.UniqueStudentsCount} طالب فعلي، {result.OrdersImported} أمر تدريب مسجل.";
        }
        catch (Exception ex)
        {
            SyncProgressText = $"حدث خطأ أثناء المزامنة: {ex.Message}";
            SyncLogs.Add($"خطأ: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    public async Task ExportMinisterialReportAsync()
    {
        IsSyncing = true;
        ExportStatusMessage = "جاري إنشاء تقرير وزارة الطيران المدني الرسمي...";

        try
        {
            string exportDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string targetPath = Path.Combine(exportDir, $"سجل_أوامر_التدريب_الرسمي_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            await _excelService.ExportOfficialMinistryReportAsync(targetPath);
            ExportStatusMessage = $"تم تصدير التقرير الوزاري بنجاح إلى سطح المكتب:\n{targetPath}";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"فشل التصدير: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }
}
