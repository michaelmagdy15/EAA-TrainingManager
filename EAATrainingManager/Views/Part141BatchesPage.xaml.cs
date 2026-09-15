using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;

namespace EAATrainingManager.Views;

public sealed partial class Part141BatchesPage : Page
{
    private readonly ObservableCollection<Part141Batch> _batches = new();

    public Part141BatchesPage()
    {
        InitializeComponent();
        BatchesList.ItemsSource = _batches;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            await App.DatabaseService.InitializeAsync();
            var rows = await App.DatabaseService.GetPart141BatchesAsync(searchQuery: SearchBox?.Text);
            _batches.Clear();
            foreach (var row in rows) _batches.Add(row);
            CountText.Text = $"{_batches.Count} دفعة";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Part141BatchesPage] {ex}");
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await LoadAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
}
