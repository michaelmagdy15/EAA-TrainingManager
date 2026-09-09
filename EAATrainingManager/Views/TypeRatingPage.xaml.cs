using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.ViewModels;

namespace EAATrainingManager.Views;

public sealed partial class TypeRatingPage : Page
{
    public TypeRatingViewModel ViewModel { get; }
    private bool _isLoaded = false;

    public TypeRatingPage()
    {
        ViewModel = new TypeRatingViewModel(App.DatabaseService);
        InitializeComponent();
        Loaded += TypeRatingPage_Loaded;
    }

    private async void TypeRatingPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ApplyLocalization();
        await LoadDataAsync();
    }

    private void ApplyLocalization()
    {
        var loc = EAATrainingManager.Services.LocalizationService.Instance;
        if (TxtHeaderTitle != null)
            TxtHeaderTitle.Text = loc.Text("تجديد طراز وبناء ساعات طيران (Type Rating & Hour Building)", "Type Rating & Hour Building Programs");
        if (TxtHeaderDesc != null)
            TxtHeaderDesc.Text = loc.Text("المسار التشغيلي الخاص بالطيارين التجاريين لتجديد وفروق الطراز وباقات بناء الساعات للطلبة والوافدين على أسطول الأكاديمية",
                                           "Operational stream for commercial pilots type ratings, renewal differences, and flight hour packages.");
        if (TxtCard1Title != null)
            TxtCard1Title.Text = loc.Text("إجمالي الطيارين المسجلين", "Total Registered Pilots");
        if (TxtCard1Desc != null)
            TxtCard1Desc.Text = loc.Text("طيار ومتدرب على أسطول الأكاديمية", "Pilots and trainees on academy fleet");
        if (TxtCard2Title != null)
            TxtCard2Title.Text = loc.Text("تجديد وفروق الطراز", "Type Ratings & Renewals");
        if (TxtCard2Desc != null)
            TxtCard2Desc.Text = loc.Text("أوامر تجديد صلاحية وفروق طراز", "Orders for ratings & differences");
        if (TxtCard3Title != null)
            TxtCard3Title.Text = loc.Text("بناء ساعات طيران", "Flight Hour Building");
        if (TxtCard3Desc != null)
            TxtCard3Desc.Text = loc.Text("باقات ساعات للطلبة والوافدين", "Hour packages for domestic & foreign pilots");
        if (TxtCard4Title != null)
            TxtCard4Title.Text = loc.Text("قيد التنفيذ العملي", "Active in Training");
        if (TxtCard4Desc != null)
            TxtCard4Desc.Text = loc.Text("رحلات طيران ومحاكي جارية", "Ongoing flights & simulator sorties");
        
        if (SearchBox != null)
            SearchBox.PlaceholderText = loc.Text("بحث باسم الطيار، رقم أمر التدريب، أو ملاحظات الطائرة...", "Search by pilot name, order number, or aircraft...");
        if (TxtActivityLabel != null)
            TxtActivityLabel.Text = loc.Text("نوع النشاط:", "Activity Type:");
        if (TxtAircraftLabel != null)
            TxtAircraftLabel.Text = loc.Text("طراز الطائرة:", "Aircraft Fleet:");

        if (ComboItemActivityAll != null) ComboItemActivityAll.Content = loc.Text("الكل", "All Activities");
        if (ComboItemActivityRating != null) ComboItemActivityRating.Content = loc.Text("تجديد طراز", "Type Rating");
        if (ComboItemActivityHours != null) ComboItemActivityHours.Content = loc.Text("بناء ساعات", "Hour Building");
        if (ComboItemAircraftAll != null) ComboItemAircraftAll.Content = loc.Text("كافة الطرازات", "All Fleets");

        if (Col0 != null) Col0.Text = loc.Text("م", "#");
        if (Col1 != null) Col1.Text = loc.Text("اسم الطيار / المتدرب", "Pilot / Trainee Name");
        if (Col2 != null) Col2.Text = loc.Text("الجنسية", "Nationality");
        if (Col3 != null) Col3.Text = loc.Text("رقم الأمر", "Order #");
        if (Col4 != null) Col4.Text = loc.Text("البرنامج التدريبي", "Training Program");
        if (Col5 != null) Col5.Text = loc.Text("تاريخ البدء", "Start Date");
        if (Col6 != null) Col6.Text = loc.Text("تاريخ النهاية", "End Date");
        if (Col7 != null) Col7.Text = loc.Text("الحالة", "Status");
        if (Col8 != null) Col8.Text = loc.Text("الإجراء", "Action");
        if (Col9 != null) Col9.Text = loc.Text("ملاحظات وساعات الطيران", "Notes & Flight Hours");
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        try
        {
            if (OrdersListView == null || TxtTotalPilots == null) return;
            await ViewModel.LoadOrdersAsync();

            OrdersListView.ItemsSource = ViewModel.Orders;
            TxtTotalPilots.Text = ViewModel.TotalPilotsCount.ToString();
            TxtTypeRatingCount.Text = ViewModel.TypeRatingCount.ToString();
            TxtHourBuildingCount.Text = ViewModel.HourBuildingCount.ToString();
            TxtActiveCount.Text = ViewModel.ActiveCount.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TypeRatingPage.LoadDataAsync] {ex}");
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || SearchBox == null) return;
        ViewModel.SearchQuery = SearchBox.Text;
        await LoadDataAsync();
    }

    private async void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        if (ActivityTypeCombo?.SelectedItem is ComboBoxItem actItem)
        {
            ViewModel.SelectedActivityType = actItem.Content?.ToString() ?? "الكل";
        }

        if (AircraftFleetCombo?.SelectedItem is ComboBoxItem acItem)
        {
            string? tag = acItem.Tag?.ToString();
            ViewModel.SelectedAircraft = (string.IsNullOrWhiteSpace(tag) || tag == "All") ? "كافة الطرازات" : tag;
        }

        await LoadDataAsync();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async void BtnToggleStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TrainingOrder order)
        {
            await ViewModel.ToggleStatusAsync(order);
            await LoadDataAsync();
        }
    }
}
