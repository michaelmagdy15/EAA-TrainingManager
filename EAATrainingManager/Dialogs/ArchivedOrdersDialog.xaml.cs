using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Dialogs;

public sealed partial class ArchivedOrdersDialog : ContentDialog
{
    private readonly DatabaseService _databaseService;
    private readonly ExcelMirrorService? _excelMirrorService;

    public bool HasChanged { get; private set; }

    public ArchivedOrdersDialog(DatabaseService databaseService, ExcelMirrorService? mirrorService = null)
    {
        _databaseService = databaseService;
        _excelMirrorService = mirrorService;

        InitializeComponent();
        Loaded += ArchivedOrdersDialog_Loaded;
    }

    private async void ArchivedOrdersDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadArchivedAsync();
    }

    private async System.Threading.Tasks.Task LoadArchivedAsync()
    {
        try
        {
            var list = await _databaseService.GetArchivedOrdersAsync();
            ArchivedListView.ItemsSource = list;
            TxtArchivedCount.Text = $"{list.Count} سجل مؤرشف في سلة الأمان";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArchivedOrdersDialog.LoadArchivedAsync] {ex.Message}");
        }
    }

    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TrainingOrder order)
        {
            await _databaseService.RestoreOrderAsync(order.Id);
            HasChanged = true;
            _excelMirrorService?.QueueMirrorSync();
            await LoadArchivedAsync();
        }
    }
}
