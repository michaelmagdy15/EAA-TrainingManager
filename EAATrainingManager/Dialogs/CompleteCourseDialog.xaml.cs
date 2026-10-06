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
            if (notes.Length < 8)
                throw new ArgumentException("Provide a reason of at least eight characters for this approval.");
            if (App.IdentityService.CurrentSession is not UserSession session)
                throw new UnauthorizedAccessException("An authenticated approval session is required.");

            await App.IdentityService.RecordApprovalAsync(
                session.SessionId,
                "TrainingOrder",
                _order.Id,
                "Complete",
                notes,
                ApprovalPassword.Password);

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
            AppLogService.LogException("TrainingOrder.CompleteApproval", ex, "TrainingOrder", _order.Id.ToString());
            ApprovalErrorText.Text = LocalizationService.Instance.Text(
                "تعذر اعتماد إتمام التدريب. تحقق من الصلاحية وكلمة المرور وسبب الاعتماد.",
                "Could not approve course completion. Check permission, password, and approval reason.");
            ApprovalErrorText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
