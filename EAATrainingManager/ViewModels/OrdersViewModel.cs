using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.ViewModels;

public partial class OrdersViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedStatusFilter = "الجميع";

    [ObservableProperty]
    private string _selectedMilestoneFilter = "الجميع";

    [ObservableProperty]
    private string _selectedRegulatoryTrack = "الجميع";

    [ObservableProperty]
    private bool _internationalOnly = false;

    [ObservableProperty]
    private string _trackTitle = "كافة أوامر التدريب";

    [ObservableProperty]
    private int _selectedYear = 0;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _filteredCount;

    public ObservableCollection<TrainingOrder> Orders { get; } = new();

    public OrdersViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    [RelayCommand]
    public async Task LoadOrdersAsync()
    {
        IsLoading = true;
        try
        {
            int? year = SelectedYear > 0 ? SelectedYear : null;
            string? prog = SelectedMilestoneFilter != "الجميع" ? SelectedMilestoneFilter : null;
            string? status = SelectedStatusFilter != "الجميع" ? SelectedStatusFilter : null;
            string? track = SelectedRegulatoryTrack != "الجميع" ? SelectedRegulatoryTrack : null;

            var list = await _dbService.GetAllOrdersAsync(year, prog, status, SearchQuery, track, InternationalOnly ? true : null);
            Orders.Clear();
            foreach (var o in list)
            {
                Orders.Add(o);
            }
            FilteredCount = Orders.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleOrderCompletionAsync(TrainingOrder order)
    {
        if (order == null) return;

        // If currently active (no completion date), complete it as of today
        // If completed, clear completion date to return to active cockpit status
        DateTime? newDate = order.IsActive ? DateTime.Today : null;
        await _dbService.UpdateOrderCompletionDateAsync(order.Id, newDate);

        // Reload orders
        await LoadOrdersAsync();
    }
}
