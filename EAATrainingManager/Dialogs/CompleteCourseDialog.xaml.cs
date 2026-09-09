using System;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Dialogs;

public sealed partial class CompleteCourseDialog : ContentDialog
{
    private readonly TrainingOrder _order;
    private readonly DatabaseService _databaseService;
    private readonly ExcelMirrorService? _excelMirrorService;
    private readonly BackupService? _backupService;

    public bool IsCompletedSuccess { get; private set; }

    public CompleteCourseDialog(
        TrainingOrder order, 
        DatabaseService databaseService, 
        ExcelMirrorService? mirrorService = null, 
        BackupService? backupService = null)
    {
        _order = order;
        _databaseService = databaseService;
        _excelMirrorService = mirrorService;
        _backupService = backupService;

        InitializeComponent();

        TxtCadetInfo.Text = $"{order.StudentDisplayName} — {order.ProgramType}";
        TxtOrderSummary.Text = $"أمر تدريب رقم: {order.OrderNumber} | المسار: {order.RegulatoryTrackBadgeText} | سنة {order.Year}";
        PickerCompletionDate.Date = DateTimeOffset.Now;
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            DateTime completionDate = PickerCompletionDate.Date.HasValue
                ? PickerCompletionDate.Date.Value.DateTime
                : DateTime.Now;

            string notes = TxtCompletionNotes.Text.Trim();

            // 1. Complete order in SQLite
            bool success = await _databaseService.CompleteOrderAsync(_order.Id, completionDate, notes);
            if (success)
            {
                _order.CompletionDate = completionDate;
                IsCompletedSuccess = true;

                // 2. Auto-Mirror to Excel in background
                _excelMirrorService?.QueueMirrorSync();

                // 3. Automated Backup Snapshot
                _ = _backupService?.CreateSnapshotAsync("AfterCourseCompletion");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CompleteCourseDialog] {ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
