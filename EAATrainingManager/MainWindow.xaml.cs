using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using EAATrainingManager.Models;
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

        NavView.Visibility = Visibility.Collapsed;
        ContentFrame.Visibility = Visibility.Collapsed;
        BtnTitleAddOrder.Visibility = Visibility.Collapsed;
        BtnTitleArchive.Visibility = Visibility.Collapsed;
        BtnTitleExcelImport.Visibility = Visibility.Collapsed;
        BtnTitleUsers.Visibility = Visibility.Collapsed;
        RootGrid.Loaded += MainWindow_Loaded;

        // Smooth dismissal of startup loading screen
        DismissLoadingAnimation();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UserSession? session = App.IdentityService.CurrentSession;
        if (session == null || await App.IdentityService.GetActiveSessionAsync(session.SessionId) == null)
        {
            if (Application.Current is App app)
                app.ReturnToLogin();
            return;
        }

        bool dashboard = await App.IdentityService.HasPermissionAsync(session.SessionId, "dashboard", PermissionLevel.ReadOnly);
        bool students = await App.IdentityService.HasPermissionAsync(session.SessionId, "students", PermissionLevel.ReadOnly);
        bool orders = await App.IdentityService.HasPermissionAsync(session.SessionId, "training-orders", PermissionLevel.ReadOnly);
        bool schedule = await App.IdentityService.HasPermissionAsync(session.SessionId, "schedule", PermissionLevel.ReadOnly);
        bool resources = await App.IdentityService.HasPermissionAsync(session.SessionId, "resources", PermissionLevel.ReadOnly);
        bool compliance = await App.IdentityService.HasPermissionAsync(session.SessionId, "compliance", PermissionLevel.ReadOnly);
        bool flights = await App.IdentityService.HasPermissionAsync(session.SessionId, "flight-records", PermissionLevel.ReadOnly);
        bool assessments = await App.IdentityService.HasPermissionAsync(session.SessionId, "assessments", PermissionLevel.ReadOnly);
        bool curriculumRead = await App.IdentityService.HasPermissionAsync(session.SessionId, "curriculum", PermissionLevel.ReadOnly);
        bool audit = await App.IdentityService.HasPermissionAsync(session.SessionId, "audit", PermissionLevel.ReadOnly);
        bool editOrders = await App.IdentityService.HasPermissionAsync(session.SessionId, "training-orders", PermissionLevel.FullEdit);
        bool userRead = await App.IdentityService.HasPermissionAsync(session.SessionId, "users", PermissionLevel.ReadOnly);

        NavItemDashboard.Visibility = dashboard ? Visibility.Visible : Visibility.Collapsed;
        NavHeaderStreams.Visibility = orders ? Visibility.Visible : Visibility.Collapsed;
        NavItemPart61.Visibility = orders ? Visibility.Visible : Visibility.Collapsed;
        NavItemPart141.Visibility = orders ? Visibility.Visible : Visibility.Collapsed;
        NavItemATP.Visibility = orders ? Visibility.Visible : Visibility.Collapsed;
        NavItemTypeRating.Visibility = orders ? Visibility.Visible : Visibility.Collapsed;
        NavItemEvaluation.Visibility = orders ? Visibility.Visible : Visibility.Collapsed;
        NavItemStudents.Visibility = students ? Visibility.Visible : Visibility.Collapsed;
        NavItemOrders.Visibility = orders ? Visibility.Visible : Visibility.Collapsed;
        NavItemSchedule.Visibility = schedule ? Visibility.Visible : Visibility.Collapsed;
        NavItemResources.Visibility = resources ? Visibility.Visible : Visibility.Collapsed;
        NavItemCompliance.Visibility = compliance ? Visibility.Visible : Visibility.Collapsed;
        NavItemFlightRecords.Visibility = flights ? Visibility.Visible : Visibility.Collapsed;
        NavItemAssessments.Visibility = assessments ? Visibility.Visible : Visibility.Collapsed;
NavItemExcelSync.Visibility = editOrders ? Visibility.Visible : Visibility.Collapsed;
        NavItemAudit.Visibility = audit ? Visibility.Visible : Visibility.Collapsed;
        NavItemCurriculum.Visibility = curriculumRead ? Visibility.Visible : Visibility.Collapsed;
        NavItemProgress.Visibility = (curriculumRead && orders && students) ? Visibility.Visible : Visibility.Collapsed;
        BtnTitleAddOrder.Visibility = editOrders ? Visibility.Visible : Visibility.Collapsed;
        BtnTitleArchive.Visibility = editOrders ? Visibility.Visible : Visibility.Collapsed;
        BtnTitleExcelImport.Visibility = editOrders ? Visibility.Visible : Visibility.Collapsed;
        BtnTitleUsers.Visibility = userRead ? Visibility.Visible : Visibility.Collapsed;

        var allowedItems = new[]
        {
            NavItemDashboard, NavItemSchedule, NavItemStudents, NavItemOrders, NavItemFlightRecords,
            NavItemResources, NavItemCompliance, NavItemAssessments, NavItemPart61, NavItemPart141,
NavItemATP, NavItemTypeRating, NavItemEvaluation, NavItemExcelSync, NavItemAudit,
            NavItemCurriculum, NavItemProgress
        };
        foreach (var item in allowedItems)
            item.IsSelected = false;

        var firstAllowed = Array.Find(allowedItems, item => item.Visibility == Visibility.Visible);
        NavView.Visibility = Visibility.Visible;
        ContentFrame.Visibility = Visibility.Visible;
        if (firstAllowed != null)
        {
            firstAllowed.IsSelected = true;
            NavView.SelectedItem = firstAllowed;
        }
        else
        {
            ContentFrame.Content = new TextBlock
            {
                Text = LocalizationService.Instance.Text("لا توجد صلاحيات تشغيلية مرتبطة بهذا الحساب.", "This account has no operational permissions."),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
        }
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
                "Schedule" => typeof(SchedulePage),
                "Resources" => typeof(ResourcesPage),
                "Compliance" => typeof(CompliancePage),
                "FlightRecords" => typeof(FlightRecordsPage),
                "Assessments" => typeof(AssessmentsPage),
                "Part61" => typeof(OrdersPage),
                "Part141" => typeof(Part141BatchesPage),
                "ATP" => typeof(ETPBatchesPage),
                "TypeRating" => typeof(TypeRatingPage),
                "Evaluation" => typeof(OrdersPage),
"ExcelSync" => typeof(ExcelSyncPage),
                "Audit" => typeof(AuditPage),
                "Curriculum" => typeof(CurriculumPage),
                "Progress" => typeof(ProgressPage),
                _ => typeof(DashboardPage)
            };

            object? parameter = tag switch
            {
                "Part61" => "Part61",
                "Part141" => null,
                "ATP" => null,
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

    private async void BtnTitleSignOut_Click(object sender, RoutedEventArgs e)
    {
        UserSession? session = App.IdentityService.CurrentSession;
        if (session != null)
            await App.IdentityService.EndSessionAsync(session.SessionId);
        if (Application.Current is App app)
            app.ReturnToLogin();
    }

    private async void BtnTitleUsers_Click(object sender, RoutedEventArgs e)
    {
        UserSession? session = App.IdentityService.CurrentSession;
        if (session == null || !await App.IdentityService.HasPermissionAsync(session.SessionId, "users", PermissionLevel.ReadOnly))
            return;

        bool canManage = await App.IdentityService.HasPermissionAsync(session.SessionId, "users", PermissionLevel.Approve);
        bool canManageLocations = await App.IdentityService.HasPermissionAsync(session.SessionId, "locations", PermissionLevel.Approve);
        var userList = new ListView { DisplayMemberPath = nameof(UserAccount.UserName), MinHeight = 120 };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var fields = new StackPanel { Spacing = 8 };
        fields.Children.Add(new TextBlock { Text = LocalizationService.Instance.Text("الحسابات الحالية", "Current accounts"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        fields.Children.Add(userList);

        TextBox? userName = null;
        TextBox? displayName = null;
        PasswordBox? password = null;
        ComboBox? role = null;
        ComboBox? userLocation = null;
        TextBox? newLocationCode = null;
        TextBox? newLocationName = null;
        if (canManage)
        {
            userName = new TextBox { PlaceholderText = LocalizationService.Instance.Text("اسم المستخدم", "Username") };
            displayName = new TextBox { PlaceholderText = LocalizationService.Instance.Text("الاسم الكامل", "Full name") };
            password = new PasswordBox { PlaceholderText = LocalizationService.Instance.Text("كلمة المرور (12 حرفاً على الأقل)", "Password (12+ characters)") };
            role = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            role.Items.Add(new ComboBoxItem { Content = "Administrator / مسؤول النظام", Tag = "administrator" });
            role.Items.Add(new ComboBoxItem { Content = "Training Manager / مدير التدريب", Tag = "training_manager" });
            role.Items.Add(new ComboBoxItem { Content = "Dispatch / العمليات", Tag = "dispatch" });
            role.Items.Add(new ComboBoxItem { Content = "Instructor / مدرب", Tag = "instructor" });
            role.Items.Add(new ComboBoxItem { Content = "Examiner / ممتحن", Tag = "examiner" });
            role.Items.Add(new ComboBoxItem { Content = "Finance / مالية", Tag = "finance" });
            role.Items.Add(new ComboBoxItem { Content = "Maintenance / صيانة", Tag = "maintenance" });
            role.Items.Add(new ComboBoxItem { Content = "Student / متدرب", Tag = "student" });
            role.Items.Add(new ComboBoxItem { Content = "Auditor / مراجع", Tag = "auditor" });
            role.Items.Add(new ComboBoxItem { Content = "Regulatory Compliance / مسؤول الامتثال", Tag = "regulatory_compliance" });
            role.SelectedIndex = 2;
            userLocation = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (LocationRecord locationRecord in await App.IdentityService.GetLocationsAsync())
                userLocation.Items.Add(new ComboBoxItem { Content = locationRecord.DisplayName, Tag = locationRecord.LocationCode });
            if (userLocation.Items.Count > 0) userLocation.SelectedIndex = 0;
            fields.Children.Add(new TextBlock { Text = LocalizationService.Instance.Text("إنشاء حساب", "Create account"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
            fields.Children.Add(userName);
            fields.Children.Add(displayName);
            fields.Children.Add(password);
            fields.Children.Add(role);
            fields.Children.Add(userLocation);
        }
        if (canManageLocations)
        {
            fields.Children.Add(new TextBlock { Text = LocalizationService.Instance.Text("إضافة موقع تشغيل", "Add operating location"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
            newLocationCode = new TextBox { PlaceholderText = LocalizationService.Instance.Text("رمز الموقع", "Location code") };
            newLocationName = new TextBox { PlaceholderText = LocalizationService.Instance.Text("اسم الموقع بالعربية والإنجليزية", "Location name") };
            var addLocationButton = new Button { Content = LocalizationService.Instance.Text("إضافة الموقع", "Add location"), HorizontalAlignment = HorizontalAlignment.Stretch };
            fields.Children.Add(newLocationCode);
            fields.Children.Add(newLocationName);
            fields.Children.Add(addLocationButton);
            addLocationButton.Click += async (_, _) =>
            {
                try
                {
                    await App.IdentityService.CreateLocationAsync(session.SessionId, newLocationCode.Text, newLocationName.Text);
                    var locations = await App.IdentityService.GetLocationsAsync();
                    if (userLocation != null)
                    {
                        userLocation.Items.Clear();
                        foreach (LocationRecord locationRecord in locations)
                            userLocation.Items.Add(new ComboBoxItem { Content = locationRecord.DisplayName, Tag = locationRecord.LocationCode });
                        userLocation.SelectedIndex = locations.FindIndex(locationRecord => string.Equals(locationRecord.LocationCode, newLocationCode.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                    }
                    status.Text = LocalizationService.Instance.Text("تمت إضافة الموقع وإتاحته للمسؤولين.", "Location added and assigned to administrators.");
                }
                catch (Exception ex)
                {
                    AppLogService.LogException("Identity.CreateLocation.UI", ex, "Location", newLocationCode.Text.Trim());
                    status.Text = LocalizationService.Instance.Text("تعذر إضافة الموقع.", "Could not add location.");
                }
            };
        }
        fields.Children.Add(status);

        async Task RefreshUsersAsync()
        {
            var users = await App.IdentityService.GetUsersAsync(session.SessionId);
            userList.ItemsSource = new ObservableCollection<UserAccount>(users);
        }

        await RefreshUsersAsync();
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Instance.Text("إدارة المستخدمين", "User management"),
            Content = fields,
            PrimaryButtonText = canManage ? LocalizationService.Instance.Text("إنشاء مستخدم", "Create user") : string.Empty,
            SecondaryButtonText = canManage ? LocalizationService.Instance.Text("تعطيل المحدد", "Disable selected") : string.Empty,
            CloseButtonText = LocalizationService.Instance.Text("إغلاق", "Close"),
            XamlRoot = Content.XamlRoot
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (!canManage || userName == null || displayName == null || password == null || role?.SelectedItem is not ComboBoxItem selectedRole || userLocation?.SelectedItem is not ComboBoxItem selectedLocation)
                return;
            args.Cancel = true;
            try
            {
                await App.IdentityService.CreateUserAsync(session.SessionId, userName.Text, displayName.Text, password.Password, [selectedRole.Tag?.ToString() ?? string.Empty], [selectedLocation.Tag?.ToString() ?? string.Empty]);
                status.Text = LocalizationService.Instance.Text("تم إنشاء الحساب.", "Account created.");
                userName.Text = string.Empty;
                displayName.Text = string.Empty;
                password.Password = string.Empty;
                await RefreshUsersAsync();
            }
            catch (Exception ex)
            {
                AppLogService.LogException("Identity.CreateUser.UI", ex, "User", userName.Text.Trim());
                status.Text = LocalizationService.Instance.Text("تعذر إنشاء الحساب. تحقق من الصلاحيات والبيانات.", "Could not create account. Check permissions and input.");
            }
        };
        dialog.SecondaryButtonClick += async (_, args) =>
        {
            if (!canManage || userList.SelectedItem is not UserAccount selectedUser)
                return;
            args.Cancel = true;
            try
            {
                if (selectedUser.Id == session.UserId)
                    throw new InvalidOperationException("The current account cannot disable itself from this dialog.");
                await App.IdentityService.SetUserActiveAsync(session.SessionId, selectedUser.Id, false);
                status.Text = LocalizationService.Instance.Text("تم تعطيل الحساب وإلغاء جلساته النشطة.", "Account disabled and active sessions revoked.");
                await RefreshUsersAsync();
            }
            catch (Exception ex)
            {
                AppLogService.LogException("Identity.DisableUser.UI", ex, "User", selectedUser.Id.ToString());
                status.Text = LocalizationService.Instance.Text("تعذر تعطيل الحساب.", "Could not disable account.");
            }
        };
        await dialog.ShowAsync();
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
        if (TxtTitleSignOut != null) TxtTitleSignOut.Text = isEn ? "Sign out" : "تسجيل الخروج";
        if (TxtTitleUsers != null) TxtTitleUsers.Text = isEn ? "Users" : "المستخدمون";
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
        if (NavItemSchedule != null) NavItemSchedule.Content = LocalizationService.Instance.NavSchedule;
        if (NavItemResources != null) NavItemResources.Content = LocalizationService.Instance.NavResources;
        if (NavItemCompliance != null) NavItemCompliance.Content = LocalizationService.Instance.NavCompliance;
        if (NavItemFlightRecords != null) NavItemFlightRecords.Content = LocalizationService.Instance.NavFlightRecords;
        if (NavItemAssessments != null) NavItemAssessments.Content = LocalizationService.Instance.NavAssessments;
        if (NavItemExcelSync != null) NavItemExcelSync.Content = LocalizationService.Instance.NavExcelSync;
        if (NavItemAudit != null) NavItemAudit.Content = LocalizationService.Instance.NavAudit;
        if (NavItemCurriculum != null) NavItemCurriculum.Content = isEn ? "Curriculum & Syllabi" : "المناهج والخطط الدراسية";
        if (NavItemProgress != null) NavItemProgress.Content = isEn ? "Trainee Progress Gateway" : "بوابة تقدم المتدرب";

        // 6. Reload current page with updated FlowDirection and language
        if (ContentFrame != null && ContentFrame.Content is Page)
        {
            ContentFrame.Navigate(_currentPageType, _currentParameter, new SuppressNavigationTransitionInfo());
        }
    }
}
