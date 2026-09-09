using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Services;

namespace EAATrainingManager.Dialogs;

public sealed partial class UpdateDialog : ContentDialog
{
    private readonly UpdateService _updateService;

    public UpdateDialog(UpdateService updateService)
    {
        _updateService = updateService;
        InitializeComponent();
        TxtCurrentVersion.Text = $"الإصدار المثبت: v{_updateService.GetCurrentVersion()} (نسخة رسمية مستقلة 100% Offline)";
    }

    private async void BtnCheckNow_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckNow.IsEnabled = false;
        CheckProgressBar.Visibility = Visibility.Visible;
        TxtUpdateStatus.Text = "جارٍ الاتصال بسيرفر التحديثات ومقارنة البصمة الرقمية للنسخة...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            TxtUpdateStatus.Text = result.Message;
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = $"تعذر التحقق من التحديثات: {ex.Message}";
        }
        finally
        {
            CheckProgressBar.Visibility = Visibility.Collapsed;
            BtnCheckNow.IsEnabled = true;
        }
    }
}
