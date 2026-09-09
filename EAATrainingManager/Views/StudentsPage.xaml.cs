using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using EAATrainingManager.Models;
using EAATrainingManager.ViewModels;
using Microsoft.UI;
using Windows.UI;

namespace EAATrainingManager.Views;

public sealed partial class StudentsPage : Page
{
    public StudentsViewModel ViewModel { get; }
    private bool _isLoaded = false;

    public StudentsPage()
    {
        ViewModel = new StudentsViewModel(App.DatabaseService);
        InitializeComponent();
        Loaded += StudentsPage_Loaded;
    }

    private async void StudentsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ApplyLocalization();
        await LoadDataAsync();
    }

    private void ApplyLocalization()
    {
        var loc = EAATrainingManager.Services.LocalizationService.Instance;
        if (TxtPageTitle != null)
            TxtPageTitle.Text = loc.Text("سجل الطلبة والمتدربين الموحد", "Unified Student & Trainee Directory");
        if (TxtPageDesc != null)
            TxtPageDesc.Text = loc.Text("الملف التعريفي الشامل لكل متدرب كشخص مستقل مع تتبع مسارات الطيران التاريخية وتفكيك تكرار الأوامر",
                                         "Comprehensive 360° profile for each unique trainee, tracking historical flight trajectories and milestones.");
        if (SearchBox != null)
            SearchBox.PlaceholderText = loc.Text("ابحث باسم المتدرب (بحث ذكي بالهمزات، الـ التعريف، وعائلة الاسم)...", "Search trainee by name, nationality, or code...");
        
        if (ComboItemAll != null) ComboItemAll.Content = loc.Text("الجميع", "All Statuses");
        if (ComboItemActive != null) ComboItemActive.Content = loc.Text("قيد التدريب", "In Training");
        if (ComboItemGraduated != null) ComboItemGraduated.Content = loc.Text("خريج", "Graduated");

        if (ColCode != null) ColCode.Text = loc.Text("كود", "Code");
        if (ColName != null) ColName.Text = loc.Text("اسم المتدرب", "Trainee Name");
        if (ColNat != null) ColNat.Text = loc.Text("الجنسية", "Nationality");
        if (ColOrders != null) ColOrders.Text = loc.Text("الأوامر", "Orders");
        if (ColStatus != null) ColStatus.Text = loc.Text("الحالة", "Status");
        if (ColMilestones != null) ColMilestones.Text = loc.Text("المراحل المجتازة", "Milestones Passed");
        if (ColTrajectoryHeader != null) ColTrajectoryHeader.Text = loc.Text("المسار 360°", "360° Trajectory");

        if (TxtTrajectoryPanelTitle != null)
            TxtTrajectoryPanelTitle.Text = loc.Text("المسار الزمني للمتدرب 360°", "360° Trainee Flight Timeline");
        if (TxtTrajectoryPanelDesc != null)
            TxtTrajectoryPanelDesc.Text = loc.Text("التسلسل التاريخي لجميع أوامر الطيران والكورسات", "Chronological sequence of all flight training orders & courses");
        if (TxtCurriculumHeader != null)
            TxtCurriculumHeader.Text = loc.Text("محطات مسار الطيران (Curriculum Pipeline):", "Curriculum Milestones Pipeline:");
        if (TxtPillPPL != null)
            TxtPillPPL.Text = loc.Text("PPL طيار خاص", "PPL Private Pilot");
        if (TxtPillCPLIR != null)
            TxtPillCPLIR.Text = loc.Text("CPL/IR تجاري وعدادات", "CPL/IR Comm & Inst");
        if (TxtPillATP != null)
            TxtPillATP.Text = loc.Text("ATP خط جوي", "ATP Airline Transport");
        if (TxtEnrolledOrdersHeader != null)
            TxtEnrolledOrdersHeader.Text = loc.Text("أوامر التدريب المسجلة لهذا المتدرب:", "Registered Training Orders for this Trainee:");
        if (TxtBtnAddOrder != null)
            TxtBtnAddOrder.Text = loc.Text("أمر تدريب جديد", "New Training Order");
    }

    private async void BtnAddOrder_Click(object sender, RoutedEventArgs e)
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

    private async void BtnCompleteOrder_Click(object sender, RoutedEventArgs e)
    {
        TrainingOrder? order = null;
        if (sender is Button btn)
        {
            order = btn.Tag as TrainingOrder ?? btn.DataContext as TrainingOrder;
        }

        if (order != null)
        {
            var dlg = new Dialogs.CompleteCourseDialog(order, App.DatabaseService, App.ExcelMirrorService, App.BackupService)
            {
                XamlRoot = this.XamlRoot
            };
            await dlg.ShowAsync();
            if (dlg.IsCompletedSuccess)
            {
                await LoadDataAsync();
                if (ViewModel.SelectedStudent != null)
                {
                    ShowTrajectory(ViewModel.SelectedStudent);
                }
            }
        }
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        try
        {
            if (StudentsListView == null || TxtStudentsCount == null) return;
            await ViewModel.LoadStudentsAsync();
            StudentsListView.ItemsSource = ViewModel.Students;
            var loc = EAATrainingManager.Services.LocalizationService.Instance;
            TxtStudentsCount.Text = loc.Text($"{ViewModel.FilteredCount} طالب مسجل", $"{ViewModel.FilteredCount} Trainees");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentsPage.LoadDataAsync] {ex}");
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || StudentsListView == null || TxtStudentsCount == null || SearchBox == null) return;
        try
        {
            ViewModel.SearchQuery = SearchBox.Text;
            await ViewModel.SearchAsync();
            StudentsListView.ItemsSource = ViewModel.Students;
            TxtStudentsCount.Text = $"{ViewModel.FilteredCount} طالب مسجل";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentsPage.SearchBox_TextChanged] {ex}");
        }
    }

    private async void StatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || StudentsListView == null || TxtStudentsCount == null || StatusFilterCombo == null) return;
        try
        {
            if (StatusFilterCombo.SelectedItem is ComboBoxItem item)
            {
                ViewModel.SelectedStatusFilter = item.Content?.ToString() ?? "الجميع";
                await ViewModel.SearchAsync();
                StudentsListView.ItemsSource = ViewModel.Students;
                TxtStudentsCount.Text = $"{ViewModel.FilteredCount} طالب مسجل";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentsPage.StatusFilterCombo_SelectionChanged] {ex}");
        }
    }

    private void StudentsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StudentsListView.SelectedItem is Student student)
        {
            ShowTrajectory(student);
        }
    }

    private void BtnViewTrajectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Student student)
        {
            ShowTrajectory(student);
        }
    }

    private async void ShowTrajectory(Student student)
    {
        try
        {
            if (student == null) return;
            await ViewModel.OpenTrajectoryAsync(student);

            if (ViewModel.SelectedStudent != null && TrajectoryPanel != null)
            {
                var s = ViewModel.SelectedStudent;
                if (TxtTrajectoryStudentName != null) TxtTrajectoryStudentName.Text = s.DisplayNameWithBiDi;
                if (TxtTrajectoryNationality != null) TxtTrajectoryNationality.Text = $"الجنسية: {s.Nationality}";
                if (TxtTrajectoryStatus != null) TxtTrajectoryStatus.Text = s.OverallStatus;
                if (TrajectoryStatusPill != null) TrajectoryStatusPill.Background = new SolidColorBrush(GetColorFromHex(s.StatusBadgeColor));

                // Milestones highlight
                if (PillPPL != null) SetPillHighlight(PillPPL, s.HasPPL);
                if (PillCPLIR != null) SetPillHighlight(PillCPLIR, s.HasCPLIR);
                if (PillATP != null) SetPillHighlight(PillATP, s.HasATP);

                if (TrajectoryOrdersItemsControl != null) TrajectoryOrdersItemsControl.ItemsSource = s.Orders;
                TrajectoryPanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentsPage.ShowTrajectory] {ex}");
        }
    }

    private void SetPillHighlight(Border pill, bool isAchieved)
    {
        if (isAchieved)
        {
            pill.Background = new SolidColorBrush(Color.FromArgb(255, 13, 110, 253)); // Blue
            if (pill.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(Colors.White);
        }
        else
        {
            pill.Background = new SolidColorBrush(Color.FromArgb(255, 233, 236, 239)); // Gray
            if (pill.Child is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.FromArgb(255, 108, 117, 125));
        }
    }

    private static Color GetColorFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#") || hex.Length < 7)
            return Colors.Gray;

        byte r = Convert.ToByte(hex.Substring(1, 2), 16);
        byte g = Convert.ToByte(hex.Substring(3, 2), 16);
        byte b = Convert.ToByte(hex.Substring(5, 2), 16);
        return Color.FromArgb(255, r, g, b);
    }

    private void BtnCloseTrajectory_Click(object sender, RoutedEventArgs e)
    {
        TrajectoryPanel.Visibility = Visibility.Collapsed;
        ViewModel.CloseTrajectory();
    }
}
