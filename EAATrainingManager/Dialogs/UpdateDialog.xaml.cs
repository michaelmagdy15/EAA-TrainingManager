using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Services;

namespace EAATrainingManager.Dialogs;

public sealed partial class UpdateDialog : ContentDialog
{
    private readonly UpdateService _updateService;
    private string _latestVersion = string.Empty;

    public UpdateDialog(UpdateService updateService)
    {
        _updateService = updateService;
        InitializeComponent();
        TxtCurrentVersion.Text = $"الإصدار المثبت: v{_updateService.GetCurrentVersion()} (نسخة رسمية مستقلة 100% Offline)";
    }

    private async void BtnCheckNow_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckNow.IsEnabled = false;
        BtnInstallNow.Visibility = Visibility.Collapsed;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        TxtDownloadDetails.Visibility = Visibility.Collapsed;
        CheckProgressBar.Visibility = Visibility.Visible;
        TxtUpdateStatus.Text = LocalizationService.Instance.Text(
            "جارٍ الاتصال بسيرفر التحديثات ومقارنة البصمة الرقمية للنسخة...",
            "Connecting to updates server and verifying digital signature...");

        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            TxtUpdateStatus.Text = result.Message;

            if (result.HasUpdate)
            {
                _latestVersion = result.LatestVersion;
                BtnInstallNow.Visibility = Visibility.Visible;
                TxtBtnInstall.Text = LocalizationService.Instance.Text(
                    $"تثبيت التحديث v{result.LatestVersion} وإعادة التشغيل الآن",
                    $"Install Update v{result.LatestVersion} & Restart Now");
            }
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

    private async void BtnInstallNow_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_latestVersion)) return;

        BtnInstallNow.IsEnabled = false;
        BtnCheckNow.IsEnabled = false;
        CloseButtonText = string.Empty; // Prevent closing while writing to disk
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        TxtDownloadDetails.Visibility = Visibility.Visible;
        TxtDownloadDetails.Text = "0%";

        bool success = await _updateService.DownloadAndInstallUpdateAsync(
            _latestVersion,
            progressCallback: (received, total, percent) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    DownloadProgressBar.Value = percent;
                    TxtDownloadDetails.Text = $"{percent}%";
                });
            },
            statusCallback: (statusText) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    TxtUpdateStatus.Text = statusText;
                });
            });

        if (!success)
        {
            CloseButtonText = LocalizationService.Instance.Text("إغلاق", "Close");
            BtnInstallNow.IsEnabled = true;
            BtnCheckNow.IsEnabled = true;
        }
    }
}
