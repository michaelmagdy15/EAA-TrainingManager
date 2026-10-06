using System;
using System.Threading.Tasks;
using EAATrainingManager.Models;
using EAATrainingManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EAATrainingManager;

public sealed partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        UpdateLanguage();
        RootGrid.Loaded += LoginWindow_Loaded;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.DatabaseService.InitializeAsync();
            await App.IdentityService.EnsureAuthorizationCatalogAsync();
            await App.CurriculumService.EnsureStreamCatalogAsync();
            foreach (LocationRecord location in await App.IdentityService.GetLocationsAsync())
            {
                LocationPicker.Items.Add(new ComboBoxItem { Content = location.DisplayName, Tag = location.LocationCode });
            }
            if (LocationPicker.Items.Count > 0)
                LocationPicker.SelectedIndex = 0;

            bool hasUsers = await App.IdentityService.HasAnyUsersAsync();
            BootstrapPanel.Visibility = hasUsers ? Visibility.Collapsed : Visibility.Visible;
            LoginPanel.Visibility = hasUsers ? Visibility.Visible : Visibility.Collapsed;
            if (hasUsers)
                LoginUserName.Focus(FocusState.Programmatic);
            else
                BootstrapUserName.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            AppLogService.LogException("LoginWindow.Initialize", ex, "IdentityStore", "Users");
            ShowStatus(LocalizationService.Instance.Text(
                "تعذر تهيئة قاعدة بيانات الهوية. راجع سجل التشخيص.",
                "Could not initialize identity storage. Check the diagnostic log."));
        }
    }

    private async void BootstrapButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(BootstrapPassword.Password, BootstrapConfirmPassword.Password, StringComparison.Ordinal))
        {
            ShowStatus(LocalizationService.Instance.Text("كلمتا المرور غير متطابقتين.", "Passwords do not match."));
            return;
        }

        try
        {
            await App.IdentityService.CreateBootstrapAdministratorAsync(
                BootstrapUserName.Text,
                BootstrapDisplayName.Text,
                BootstrapPassword.Password);
            LoginUserName.Text = BootstrapUserName.Text.Trim();
            LoginPanel.Visibility = Visibility.Visible;
            BootstrapPanel.Visibility = Visibility.Collapsed;
            await RefreshLoginLocationsAsync(LoginUserName.Text);
            LoginPassword.Focus(FocusState.Programmatic);
            ShowStatus(LocalizationService.Instance.Text("تم إنشاء المسؤول. سجّل الدخول للمتابعة.", "Administrator created. Sign in to continue."), isError: false);
        }
        catch (Exception ex)
        {
            AppLogService.LogException("Identity.BootstrapAdministrator", ex, "User", BootstrapUserName.Text.Trim());
            ShowStatus(LocalizationService.Instance.Text("تعذر إنشاء حساب المسؤول. تحقق من البيانات وسجل التشخيص.", "Could not create the administrator account. Check the input and diagnostic log."));
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        string? locationCode = (LocationPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        try
        {
            var availableLocations = await App.IdentityService.GetAvailableLocationsAsync(LoginUserName.Text);
            if (locationCode == null || !availableLocations.Exists(location => string.Equals(location.LocationCode, locationCode, StringComparison.OrdinalIgnoreCase)))
            {
                LocationPicker.Items.Clear();
                foreach (LocationRecord location in availableLocations)
                    LocationPicker.Items.Add(new ComboBoxItem { Content = location.DisplayName, Tag = location.LocationCode });
                if (LocationPicker.Items.Count > 0)
                    LocationPicker.SelectedIndex = 0;
                ShowStatus(LocalizationService.Instance.Text("تم تحميل مواقع هذا الحساب. اختر الموقع ثم سجّل الدخول.", "Loaded this account's assigned locations. Select a location and sign in."), isError: false);
                return;
            }

            UserSession? session = await App.IdentityService.AuthenticateAsync(LoginUserName.Text, LoginPassword.Password, locationCode);
            if (session == null)
            {
                ShowStatus(LocalizationService.Instance.Text("اسم المستخدم أو كلمة المرور غير صحيحة، أو أن الحساب غير نشط.", "Username/password is incorrect, or the account is disabled."));
                return;
            }

            if (Application.Current is App app)
                app.OnLoginSucceeded();
        }
        catch (Exception ex)
        {
            AppLogService.LogException("Identity.Login", ex, "User", LoginUserName.Text.Trim());
            ShowStatus(LocalizationService.Instance.Text("تعذر تسجيل الدخول. راجع سجل التشخيص.", "Sign-in failed. Check the diagnostic log."));
        }
    }

    private async void LoginUserName_LostFocus(object sender, RoutedEventArgs e)
    {
        await RefreshLoginLocationsAsync(LoginUserName.Text);
    }

    private async Task RefreshLoginLocationsAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return;
        try
        {
            var locations = await App.IdentityService.GetAvailableLocationsAsync(userName);
            LocationPicker.Items.Clear();
            foreach (LocationRecord location in locations)
                LocationPicker.Items.Add(new ComboBoxItem { Content = location.DisplayName, Tag = location.LocationCode });
            if (LocationPicker.Items.Count > 0)
                LocationPicker.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AppLogService.LogException("LoginWindow.LoadAssignedLocations", ex, "User", userName.Trim());
        }
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.ToggleLanguage();
        UpdateLanguage();
    }

    private void UpdateLanguage()
    {
        LocalizationService localization = LocalizationService.Instance;
        bool english = localization.IsEnglish;
        RootGrid.FlowDirection = localization.CurrentFlowDirection;
        Title = localization.Text("تسجيل الدخول - EAA-TMS", "Sign in - EAA-TMS");
        AcademyTitle.Text = localization.Text("الأكاديمية المصرية لعلوم الطيران", "Egyptian Aviation Academy");
        SignInSubtitle.Text = localization.Text("تسجيل الدخول إلى منظومة إدارة التدريب", "Sign in to the Training Management System");
        LoginUserName.PlaceholderText = localization.Text("اسم المستخدم", "Username");
        LoginPassword.PlaceholderText = localization.Text("كلمة المرور", "Password");
        LoginButtonText.Text = localization.Text("تسجيل الدخول", "Sign in");
        BootstrapPrompt.Text = localization.Text("إنشاء حساب مسؤول النظام الأول. استخدم كلمة مرور لا تقل عن 12 حرفاً.", "Create the first system administrator account. Use a password of at least 12 characters.");
        BootstrapUserName.PlaceholderText = localization.Text("اسم المستخدم", "Username");
        BootstrapDisplayName.PlaceholderText = localization.Text("الاسم الكامل", "Full name");
        BootstrapPassword.PlaceholderText = localization.Text("كلمة المرور (12 حرفاً على الأقل)", "Password (at least 12 characters)");
        BootstrapConfirmPassword.PlaceholderText = localization.Text("تأكيد كلمة المرور", "Confirm password");
        BootstrapButtonText.Text = localization.Text("إنشاء المسؤول الأول", "Create first administrator");
        LanguageButton.Content = english ? "العربية" : "English";
    }

    private void ShowStatus(string message, bool isError = true)
    {
        StatusText.Text = message;
        StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            isError ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.LightGreen);
        StatusText.Visibility = Visibility.Visible;
    }
}
