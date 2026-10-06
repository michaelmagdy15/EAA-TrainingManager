using System;
using EAATrainingManager.Models;
using EAATrainingManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EAATrainingManager.Dialogs;

internal static class ReopenOrderApprovalDialog
{
    public static async Task<bool> ShowAsync(TrainingOrder order, XamlRoot xamlRoot)
    {
        var reasonBox = new TextBox
        {
            PlaceholderText = LocalizationService.Instance.Text("سبب إعادة فتح الأمر (8 أحرف على الأقل)", "Reason for reopening (8+ characters)"),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64
        };
        var passwordBox = new PasswordBox
        {
            PlaceholderText = LocalizationService.Instance.Text("أعد إدخال كلمة المرور لاعتماد الإجراء", "Re-enter password to approve")
        };
        var errorText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            TextWrapping = TextWrapping.Wrap
        };
        var content = new StackPanel { Spacing = 10, MinWidth = 380 };
        content.Children.Add(new TextBlock
        {
            Text = LocalizationService.Instance.Text(
                $"إعادة فتح أمر {order.OrderNumber} للمتدرب {order.StudentDisplayName}.",
                $"Reopen order {order.OrderNumber} for {order.StudentDisplayName}."),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(reasonBox);
        content.Children.Add(passwordBox);
        content.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            Title = LocalizationService.Instance.Text("اعتماد إعادة فتح أمر التدريب", "Approve training-order reopening"),
            Content = content,
            PrimaryButtonText = LocalizationService.Instance.Text("اعتماد وإعادة الفتح", "Approve and reopen"),
            CloseButtonText = LocalizationService.Instance.Text("إلغاء", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        bool reopened = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                UserSession? session = App.IdentityService.CurrentSession;
                if (session == null)
                    throw new UnauthorizedAccessException("An authenticated approval session is required.");

                await App.IdentityService.RecordApprovalAsync(
                    session.SessionId,
                    "TrainingOrder",
                    order.Id,
                    "SetCompletionDate",
                    reasonBox.Text,
                    passwordBox.Password);
                await App.DatabaseService.UpdateOrderCompletionDateAsync(order.Id, null);
                App.ExcelMirrorService.QueueMirrorSync();
                await App.BackupService.CreateSnapshotAsync("AfterOrderReopened");
                reopened = true;
            }
            catch (Exception ex)
            {
                AppLogService.LogException("TrainingOrder.ReopenApproval.UI", ex, "TrainingOrder", order.Id.ToString());
                errorText.Text = LocalizationService.Instance.Text(
                    "تعذر إعادة فتح الأمر. تحقق من الصلاحية وكلمة المرور وسبب الاعتماد.",
                    "Could not reopen the order. Check permission, password, and approval reason.");
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
        return reopened;
    }
}
