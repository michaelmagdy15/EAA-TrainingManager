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
    private string _excelFilePath = string.Empty;

    [ObservableProperty]
    private string _selectedFileName = string.Empty;

    [ObservableProperty]
    private string _selectedFileSizeText = string.Empty;

    [ObservableProperty]
    private string _selectedFileDateText = string.Empty;

    [ObservableProperty]
    private bool _hasSelectedFile;

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
        AutoDetectDefaultFile();
    }

    public void SetFilePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            ExcelFilePath = path;
            try
            {
                var fi = new FileInfo(path);
                SelectedFileName = fi.Name;
                double kb = fi.Length / 1024.0;
                SelectedFileSizeText = kb >= 1024 ? $"{kb / 1024.0:F2} MB" : $"{kb:F0} KB";
                SelectedFileDateText = $"آخر تعديل: {fi.LastWriteTime:yyyy/MM/dd HH:mm}";
                HasSelectedFile = true;
                SyncProgressText = "جاهز للاستيراد ومعالجة البيانات بنقرة واحدة";
            }
            catch
            {
                SelectedFileName = Path.GetFileName(path);
                SelectedFileSizeText = string.Empty;
                SelectedFileDateText = string.Empty;
                HasSelectedFile = true;
                SyncProgressText = "جاهز للمزامنة";
            }
        }
        else
        {
            ExcelFilePath = string.Empty;
            SelectedFileName = string.Empty;
            SelectedFileSizeText = string.Empty;
            SelectedFileDateText = string.Empty;
            HasSelectedFile = false;
            SyncProgressText = "يرجى اختيار ملف إكسيل صالح (.xlsx)";
        }
    }

    public void AutoDetectDefaultFile()
    {
        // 1. Check workspace / standard hardcoded path if it exists
        string primaryPath = @"C:\Users\Mi5a\EAA System\2اوامر التدريب.xlsx";
        if (File.Exists(primaryPath))
        {
            SetFilePath(primaryPath);
            return;
        }

        // 2. Search common folders: current directory, base directory, EAA folder, Desktop
        string[] searchDirs = [
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.CurrentDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EAA System"),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        ];

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            string standardFile = Path.Combine(dir, "2اوامر التدريب.xlsx");
            if (File.Exists(standardFile))
            {
                SetFilePath(standardFile);
                return;
            }

            try
            {
                var candidates = Directory.GetFiles(dir, "*.xlsx");
                foreach (var c in candidates)
                {
                    string name = Path.GetFileName(c);
                    if (name.Contains("اوامر") || name.Contains("تدريب") || name.Contains("Training"))
                    {
                        SetFilePath(c);
                        return;
                    }
                }
            }
            catch { }
        }

        // Fallback: clear selection so user sees friendly browse button
        SetFilePath(null);
    }

    [RelayCommand]
    public async Task StartImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExcelFilePath) || !File.Exists(ExcelFilePath))
        {
            SyncProgressText = "الملف المحدد غير موجود! يرجى اختيار ملف إكسيل أولاً.";
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
