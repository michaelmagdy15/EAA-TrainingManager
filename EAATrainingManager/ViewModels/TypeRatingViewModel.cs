using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.ViewModels;

public partial class TypeRatingViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedActivityType = "الكل"; // "الكل", "تجديد طراز", "بناء ساعات"

    [ObservableProperty]
    private string _selectedAircraft = "كافة الطرازات"; // "كافة الطرازات", "Cessna 172", "Piper PA-28/34", "Beechcraft Duchess"

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalPilotsCount;

    [ObservableProperty]
    private int _typeRatingCount;

    [ObservableProperty]
    private int _hourBuildingCount;

    [ObservableProperty]
    private int _activeCount;

    public ObservableCollection<TrainingOrder> Orders { get; } = new();

    public TypeRatingViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    [RelayCommand]
    public async Task LoadOrdersAsync()
    {
        IsLoading = true;
        try
        {
            await _dbService.InitializeAsync();
            var allOrders = await _dbService.GetAllOrdersAsync(null, null, null, SearchQuery, "TypeRating");

            // If empty, also fallback to search by keyword in notes/program type
            if (allOrders.Count == 0)
            {
                var fallback = await _dbService.GetAllOrdersAsync(null, null, null, null);
                allOrders = fallback.Where(o => 
                    o.RegulationCategory.Contains("طراز") || 
                    o.ProgramType.Contains("طراز") || 
                    o.ProgramType.Contains("ساعات") || 
                    o.Notes.Contains("طراز") || 
                    o.Notes.Contains("ساعات") || 
                    o.Notes.Contains("CESSNA") || 
                    o.Milestone == "TYPE_RATING").ToList();
            }

            Orders.Clear();
            int trCount = 0;
            int hbCount = 0;
            int actCount = 0;

            foreach (var order in allOrders)
            {
                bool isHourBuilding = order.Notes.Contains("بناء ساعات") || order.Notes.Contains("ساعات") || order.ProgramType.Contains("ساعات");
                bool isTypeRating = !isHourBuilding;

                if (isHourBuilding) hbCount++;
                else trCount++;

                if (order.IsActive) actCount++;

                // Activity filter
                if (SelectedActivityType == "تجديد طراز" && isHourBuilding) continue;
                if (SelectedActivityType == "بناء ساعات" && !isHourBuilding) continue;

                // Aircraft filter
                if (SelectedAircraft != "كافة الطرازات")
                {
                    string orderText = $"{order.ProgramType} {order.Notes}".ToUpperInvariant();
                    if (SelectedAircraft == "Cessna 172" && !orderText.Contains("CESSNA") && !orderText.Contains("172") && !orderText.Contains("سيسنا"))
                        continue;
                    if (SelectedAircraft == "Piper PA-28/34" && !orderText.Contains("PIPER") && !orderText.Contains("PA") && !orderText.Contains("بايبير"))
                        continue;
                    if (SelectedAircraft == "Beechcraft Duchess" && !orderText.Contains("BEECH") && !orderText.Contains("DUCHESS") && !orderText.Contains("BE-76") && !orderText.Contains("دوتشيس"))
                        continue;
                }

                Orders.Add(order);
            }

            TotalPilotsCount = allOrders.Select(o => o.StudentId).Distinct().Count();
            TypeRatingCount = trCount;
            HourBuildingCount = hbCount;
            ActiveCount = actCount;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleStatusAsync(TrainingOrder order)
    {
        if (order == null) return;
        DateTime? newDate = order.IsActive ? DateTime.Today : null;
        await _dbService.UpdateOrderCompletionDateAsync(order.Id, newDate);
        await LoadOrdersAsync();
    }
}
