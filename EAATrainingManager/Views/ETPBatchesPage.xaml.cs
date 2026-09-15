using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;

namespace EAATrainingManager.Views;

public sealed partial class ETPBatchesPage : Page
{
    private readonly ObservableCollection<ETPBatch> _routes = new();
    public ETPBatchesPage()
    {
        InitializeComponent();
        RoutesList.ItemsSource = _routes;
        Loaded += async (_, _) => await LoadAsync();
    }
    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            await App.DatabaseService.InitializeAsync();
            var rows = await App.DatabaseService.GetETPBatchesAsync(searchQuery: SearchBox?.Text);
            _routes.Clear(); foreach (var row in rows) _routes.Add(row);
            CountText.Text = $"{_routes.Count} مسار";
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ETPBatchesPage] {ex}"); }
    }
    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await LoadAsync();
}
