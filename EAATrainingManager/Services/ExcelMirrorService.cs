using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EAATrainingManager.Services;

public class ExcelMirrorService
{
    private readonly DatabaseService _databaseService;
    private readonly ExcelSyncService _excelSyncService;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    public ExcelMirrorService(DatabaseService databaseService, ExcelSyncService excelSyncService)
    {
        _databaseService = databaseService;
        _excelSyncService = excelSyncService;
    }

    /// <summary>
    /// Debounces and queues a background export to master Excel mirror files
    /// </summary>
    public void QueueMirrorSync()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                // 1.5s debounce to coalesce rapid changes
                await Task.Delay(1500, token);
                if (token.IsCancellationRequested) return;

                await ExecuteMirrorSyncAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExcelMirrorService.QueueMirrorSync] {ex.Message}");
            }
        }, token);
    }

    public async Task ExecuteMirrorSyncAsync()
    {
        await _lock.WaitAsync();
        try
        {
            // 1. Local AppData shadow mirror
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(localAppData, "EAA_TrainingManager");
            string appMirrorPath = Path.Combine(appFolder, "EAA_Master_Mirror.xlsx");

            await _excelSyncService.ExportOfficialMinistryReportAsync(appMirrorPath);

            // 2. Desktop mirror for immediate user access
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktop))
                {
                    string desktopPath = Path.Combine(desktop, "EAA_Master_Mirror.xlsx");
                    File.Copy(appMirrorPath, desktopPath, true);
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExcelMirrorService.ExecuteMirrorSyncAsync] {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }
}
