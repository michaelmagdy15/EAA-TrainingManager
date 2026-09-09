using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.ViewModels;

namespace EAATrainingManager.Views;

public sealed partial class OrdersPage : Page
{
    public OrdersViewModel ViewModel { get; }
    private bool _isLoaded = false;

    public OrdersPage()
    {
        ViewModel = new OrdersViewModel(App.DatabaseService);
        InitializeComponent();
        Loaded += OrdersPage_Loaded;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var loc = EAATrainingManager.Services.LocalizationService.Instance;
        if (e.Parameter is string track && !string.IsNullOrWhiteSpace(track))
        {
            ViewModel.SelectedRegulatoryTrack = track;
            ViewModel.TrackTitle = track switch
            {
                "Part61" => loc.Text("النظام الحر - 61 (Part 61 Trainees)", "Part 61 Trainees (Modular)"),
                "Part141" => loc.Text("الدفعات المعتمدة - 141 (Part 141 Cadets)", "Part 141 Cadets (Approved Batches)"),
                "ATP" => loc.Text("خط جوي - ATP (Airline Transport Pilot)", "ATP - Airline Transport Pilot Ground School"),
                "Evaluation" => loc.Text("التقييم والمعادلات (Evaluations & Conversions)", "Evaluations & Foreign License Conversions"),
                _ => loc.Text("كافة أوامر التدريب", "All Training Orders")
            };
        }
        else
        {
            ViewModel.TrackTitle = loc.Text("كافة أوامر التدريب", "All Training Orders");
        }
    }

    private async void OrdersPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ApplyLocalization();
        await LoadDataAsync();
    }

    private void ApplyLocalization()
    {
        var loc = EAATrainingManager.Services.LocalizationService.Instance;
        if (TxtPageDesc != null)
            TxtPageDesc.Text = loc.Text("متابعة أوامر التدريب وحالات الطيران النشطة مع التبديل الفوري لحالة الإتمام (تحديث تاريخ النهاية يغلق الأمر ويحدث ملف المتدرب)",
                                         "Track training orders and active flight status with instant status toggling and trajectory sync.");
        if (SearchBox != null)
            SearchBox.PlaceholderText = loc.Text("ابحث برقم الأمر، اسم المتدرب، أو الملاحظات...", "Search by order #, trainee name, or notes...");

        if (ComboItemStatusAll != null) ComboItemStatusAll.Content = loc.Text("الجميع", "All Statuses");
        if (ComboItemStatusActive != null) ComboItemStatusActive.Content = loc.Text("قيد التدريب", "In Training");
        if (ComboItemStatusFinished != null) ComboItemStatusFinished.Content = loc.Text("منتهي", "Completed");

        if (ComboItemMilestoneAll != null) ComboItemMilestoneAll.Content = loc.Text("كافة المناهج", "All Curricula");
        if (ComboItemYearAll != null) ComboItemYearAll.Content = loc.Text("كل السنوات", "All Years");

        if (ColSeq != null) ColSeq.Text = loc.Text("م", "#");
        if (ColOrderNum != null) ColOrderNum.Text = loc.Text("رقم الأمر", "Order #");
        if (ColStudentName != null) ColStudentName.Text = loc.Text("اسم المتدرب / الطالب", "Trainee / Cadet Name");
        if (ColNationality != null) ColNationality.Text = loc.Text("الجنسية", "Nationality");
        if (ColProgram != null) ColProgram.Text = loc.Text("البرنامج / المنهج", "Program / Curriculum");
        if (ColEnrollDate != null) ColEnrollDate.Text = loc.Text("الالتحاق", "Enrolled");
        if (ColEndDate != null) ColEndDate.Text = loc.Text("النهاية", "End Date");
        if (ColStatus != null) ColStatus.Text = loc.Text("الحالة", "Status");
        if (TxtBtnAddOrder != null) TxtBtnAddOrder.Text = loc.Text("أمر تدريب جديد", "New Order");
        if (TxtBtnArchive != null) TxtBtnArchive.Text = loc.Text("سلة الأرشيف", "Archive");
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        try
        {
            if (OrdersListView == null || TxtOrdersCount == null) return;
            if (TxtPageTitle != null) TxtPageTitle.Text = ViewModel.TrackTitle;
            await ViewModel.LoadOrdersAsync();
            OrdersListView.ItemsSource = ViewModel.Orders;
            var loc = EAATrainingManager.Services.LocalizationService.Instance;
            TxtOrdersCount.Text = loc.Text($"{ViewModel.FilteredCount} أمر تدريب", $"{ViewModel.FilteredCount} Orders");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrdersPage.LoadDataAsync] {ex}");
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || OrdersListView == null || TxtOrdersCount == null || SearchBox == null) return;
        try
        {
            ViewModel.SearchQuery = SearchBox.Text;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrdersPage.SearchBox_TextChanged] {ex}");
        }
    }

    private async void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || OrdersListView == null || TxtOrdersCount == null) return;
        try
        {
            if (StatusFilterCombo?.SelectedItem is ComboBoxItem statusItem)
            {
                ViewModel.SelectedStatusFilter = statusItem.Content?.ToString() ?? "الجميع";
            }

            if (MilestoneFilterCombo?.SelectedItem is ComboBoxItem milestoneItem)
            {
                ViewModel.SelectedMilestoneFilter = milestoneItem.Content?.ToString() ?? "كافة المناهج";
                if (ViewModel.SelectedMilestoneFilter == "كافة المناهج") ViewModel.SelectedMilestoneFilter = "الجميع";
            }

            if (YearFilterCombo?.SelectedItem is ComboBoxItem yearItem)
            {
                string yrStr = yearItem.Content?.ToString() ?? "";
                ViewModel.SelectedYear = int.TryParse(yrStr, out int y) ? y : 0;
            }

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrdersPage.FilterChanged] {ex}");
        }
    }

    private async void BtnToggleStatus_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is TrainingOrder order)
            {
                await ViewModel.ToggleOrderCompletionAsync(order);
                App.ExcelMirrorService.QueueMirrorSync();
                await LoadDataAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrdersPage.BtnToggleStatus_Click] {ex}");
        }
    }

    private async void BtnCompleteOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TrainingOrder order)
        {
            var dlg = new Dialogs.CompleteCourseDialog(order, App.DatabaseService, App.ExcelMirrorService, App.BackupService)
            {
                XamlRoot = this.XamlRoot
            };
            await dlg.ShowAsync();
            if (dlg.IsCompletedSuccess)
            {
                await LoadDataAsync();
            }
        }
    }

    private async void BtnArchiveOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TrainingOrder order)
        {
            await App.DatabaseService.ArchiveOrderAsync(order.Id);
            App.ExcelMirrorService.QueueMirrorSync();
            await LoadDataAsync();
        }
    }

    private async void BtnAddOrderPage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.AddOrderDialog(App.DatabaseService, App.ExcelMirrorService, App.BackupService)
        {
            XamlRoot = this.XamlRoot
        };
        var res = await dlg.ShowAsync();
        if (res == ContentDialogResult.Primary && dlg.CreatedOrder != null)
        {
            await LoadDataAsync();
        }
    }

    private async void BtnArchivePage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.ArchivedOrdersDialog(App.DatabaseService, App.ExcelMirrorService)
        {
            XamlRoot = this.XamlRoot
        };
        await dlg.ShowAsync();
        if (dlg.HasChanged)
        {
            await LoadDataAsync();
        }
    }
}
