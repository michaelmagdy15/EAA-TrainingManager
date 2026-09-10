using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using EAATrainingManager.Services;
using EAATrainingManager.Views;

namespace EAATrainingManager;

public sealed partial class MainWindow : Window
{
    public static new MainWindow? Current { get; private set; }
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private Type _currentPageType = typeof(DashboardPage);
    private object? _currentParameter = null;

    public MainWindow()
    {
        Current = this;
        InitializeComponent();

        // Set official EAA icon, resize, and center window
        ConfigureWindow();

        // Listen for language changes
        LocalizationService.Instance.LanguageChanged += UpdateLanguageUI;
        UpdateLanguageUI();

        // Initial navigation
        ContentFrame.Navigate(typeof(DashboardPage), null, new EntranceNavigationTransitionInfo());

        // Smooth dismissal of startup loading screen
        DismissLoadingAnimation();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void ConfigureWindow()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _appWindow?.SetIcon(iconPath);
            }

            if (_appWindow != null)
            {
                if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.IsMinimizable = true;
                    presenter.IsMaximizable = true;
                    presenter.IsResizable = true;
                }

                _appWindow.Resize(new Windows.Graphics.SizeInt32(1380, 880));
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var centeredPosition = _appWindow.Position;
                    centeredPosition.X = (displayArea.WorkArea.Width - 1380) / 2;
                    centeredPosition.Y = (displayArea.WorkArea.Height - 880) / 2;
                    _appWindow.Move(centeredPosition);
                }
                _appWindow.Show(true);
            }

            ShowWindow(hWnd, 1); // SW_SHOWNORMAL
            SetForegroundWindow(hWnd);
        }
        catch { }
    }

    private void DismissLoadingAnimation()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            try
            {
                var fadeAnim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(400)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };

                var storyboard = new Storyboard();
                storyboard.Children.Add(fadeAnim);
                Storyboard.SetTarget(fadeAnim, StartupLoadingOverlay);
                Storyboard.SetTargetProperty(fadeAnim, "Opacity");

                storyboard.Completed += (s2, e2) =>
                {
                    StartupLoadingOverlay.Visibility = Visibility.Collapsed;
                };

                storyboard.Begin();
            }
            catch
            {
                StartupLoadingOverlay.Visibility = Visibility.Collapsed;
            }
        };
        timer.Start();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem selectedItem)
        {
            string tag = selectedItem.Tag?.ToString() ?? "Dashboard";
            Type pageType = tag switch
            {
                "Dashboard" => typeof(DashboardPage),
                "Students" => typeof(StudentsPage),
                "Orders" => typeof(OrdersPage),
                "Part61" => typeof(OrdersPage),
                "Part141" => typeof(OrdersPage),
                "ATP" => typeof(OrdersPage),
                "TypeRating" => typeof(TypeRatingPage),
                "Evaluation" => typeof(OrdersPage),
                "ExcelSync" => typeof(ExcelSyncPage),
                _ => typeof(DashboardPage)
            };

            object? parameter = tag switch
            {
                "Part61" => "Part61",
                "Part141" => "Part141",
                "ATP" => "ATP",
                "Evaluation" => "Evaluation",
                _ => null
            };

            _currentPageType = pageType;
            _currentParameter = parameter;

            try
            {
                var effect = LocalizationService.Instance.IsEnglish ? SlideNavigationTransitionEffect.FromLeft : SlideNavigationTransitionEffect.FromRight;
                ContentFrame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo { Effect = effect });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation Exception] Page: {pageType.Name}, Tag: {tag}, Error: {ex}");
            }
        }
    }

    private async void BtnTitleAddOrder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.AddOrderDialog(App.DatabaseService, App.ExcelMirrorService, App.BackupService)
        {
            XamlRoot = this.Content.XamlRoot
        };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary && dlg.CreatedOrder != null)
        {
            if (ContentFrame != null && ContentFrame.Content is Page)
            {
                ContentFrame.Navigate(_currentPageType, _currentParameter, new SuppressNavigationTransitionInfo());
            }
        }
    }

    private async void BtnTitleArchive_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.ArchivedOrdersDialog(App.DatabaseService, App.ExcelMirrorService)
        {
            XamlRoot = this.Content.XamlRoot
        };
        await dlg.ShowAsync();
        if (dlg.HasChanged)
        {
            if (ContentFrame != null && ContentFrame.Content is Page)
            {
                ContentFrame.Navigate(_currentPageType, _currentParameter, new SuppressNavigationTransitionInfo());
            }
        }
    }

    private async void BtnTitleUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.UpdateDialog(App.UpdateService)
        {
            XamlRoot = this.Content.XamlRoot
        };
        await dlg.ShowAsync();
    }

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        _currentPageType = pageType;
        _currentParameter = parameter;
        try
        {
            var effect = LocalizationService.Instance.IsEnglish ? SlideNavigationTransitionEffect.FromLeft : SlideNavigationTransitionEffect.FromRight;
            ContentFrame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo { Effect = effect });

            if (pageType == typeof(ExcelSyncPage) && NavItemExcelSync != null)
            {
                NavView.SelectedItem = NavItemExcelSync;
            }
            else if (pageType == typeof(DashboardPage) && NavItemDashboard != null)
            {
                NavView.SelectedItem = NavItemDashboard;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow.NavigateTo] {ex}");
        }
    }

    private void BtnTitleExcelImport_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(typeof(ExcelSyncPage));
    }

    private void BtnTitleMinimize_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
            else
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ShowWindow(hWnd, 6); // SW_MINIMIZE = 6
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BtnTitleMinimize_Click] {ex}");
        }
    }

    private void BtnLanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.ToggleLanguage();
    }

    private void UpdateLanguageUI()
    {
        bool isEn = LocalizationService.Instance.IsEnglish;
        var dir = LocalizationService.Instance.CurrentFlowDirection;

        // 1. Root & Navigation Flow Direction (Mirrors entire app layout!)
        if (RootGrid != null) RootGrid.FlowDirection = dir;
        if (NavView != null) NavView.FlowDirection = dir;
        if (ContentFrame != null) ContentFrame.FlowDirection = dir;

        // 2. Window Title & Header
        this.Title = LocalizationService.Instance.WindowTitle;
        if (TxtAppTitle != null) TxtAppTitle.Text = LocalizationService.Instance.AppTitle;

        // 3. Action Buttons
        if (TxtTitleExcelImport != null) TxtTitleExcelImport.Text = isEn ? "Import Excel" : "استيراد إكسيل";
        if (TxtTitleAddOrder != null) TxtTitleAddOrder.Text = isEn ? "New Order" : "أمر تدريب جديد";
        if (TxtTitleArchive != null) TxtTitleArchive.Text = isEn ? "Archive" : "الأرشيف";
        if (TxtTitleUpdate != null) TxtTitleUpdate.Text = isEn ? "Updates" : "تحديثات";
        if (TxtTitleMinimize != null) TxtTitleMinimize.Text = isEn ? "Minimize" : "تصغير";
        if (TxtTitleLangBadge != null) TxtTitleLangBadge.Text = isEn ? "العربية" : "English";
        if (TxtPaneLangLabel != null) TxtPaneLangLabel.Text = isEn ? "Switch to العربية" : "تغيير اللغة (English)";
        if (TxtPaneLangCode != null) TxtPaneLangCode.Text = isEn ? "AR" : "EN";

        // 4. Header & Footer Labels
        if (TxtDirectorate != null) TxtDirectorate.Text = LocalizationService.Instance.DirectorateName;
        if (TxtAcademy != null) TxtAcademy.Text = LocalizationService.Instance.AcademySubTitle;
        if (TxtOfflineStatus != null) TxtOfflineStatus.Text = LocalizationService.Instance.OfflineBadge;

        // 5. Navigation Menu Items
        if (NavItemDashboard != null) NavItemDashboard.Content = LocalizationService.Instance.NavDashboard;
        if (NavHeaderStreams != null) NavHeaderStreams.Content = LocalizationService.Instance.NavStreamsHeader;
        if (NavItemPart61 != null) NavItemPart61.Content = LocalizationService.Instance.NavPart61;
        if (NavItemPart141 != null) NavItemPart141.Content = LocalizationService.Instance.NavPart141;
        if (NavItemATP != null) NavItemATP.Content = LocalizationService.Instance.NavATP;
        if (NavItemTypeRating != null) NavItemTypeRating.Content = LocalizationService.Instance.NavTypeRating;
        if (NavItemEvaluation != null) NavItemEvaluation.Content = LocalizationService.Instance.NavEvaluation;
        if (NavItemStudents != null) NavItemStudents.Content = LocalizationService.Instance.NavStudents;
        if (NavItemOrders != null) NavItemOrders.Content = LocalizationService.Instance.NavOrders;
        if (NavItemExcelSync != null) NavItemExcelSync.Content = LocalizationService.Instance.NavExcelSync;

        // 6. Reload current page with updated FlowDirection and language
        if (ContentFrame != null && ContentFrame.Content is Page)
        {
            ContentFrame.Navigate(_currentPageType, _currentParameter, new SuppressNavigationTransitionInfo());
        }
    }
}
