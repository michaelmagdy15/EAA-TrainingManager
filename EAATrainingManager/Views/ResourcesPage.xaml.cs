using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Views;

public sealed partial class ResourcesPage : Page
{
    private readonly ObservableCollection<AircraftResource> _resources = new();
    public ResourcesPage()
    {
        InitializeComponent();
        ResourcesList.ItemsSource = _resources;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        Loaded += async (_, _) => { ApplyLocalization(); await LoadAsync(); };
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            await App.DatabaseService.InitializeAsync();
            var resources = await App.DatabaseService.GetAircraftResourcesAsync();
            _resources.Clear();
            foreach (var resource in resources) _resources.Add(resource);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            string type = (ResourceTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Aircraft";
            string status = (StatusCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Available";
            await App.DatabaseService.SaveAircraftResourceAsync(new AircraftResource
            {
                Registration = RegistrationBox.Text,
                ResourceType = type,
                AircraftType = AircraftTypeBox.Text,
                Base = BaseBox.Text,
                Status = status,
                HobbsHours = double.IsNaN(HobbsBox.Value) ? 0 : HobbsBox.Value,
                TachHours = double.IsNaN(TachBox.Value) ? 0 : TachBox.Value,
                MaintenanceDueAtHours = double.IsNaN(MaintenanceDueBox.Value) ? null : MaintenanceDueBox.Value,
                Notes = NotesBox.Text
            });
            RegistrationBox.Text = string.Empty;
            AircraftTypeBox.Text = string.Empty;
            BaseBox.Text = string.Empty;
            HobbsBox.Value = 0;
            TachBox.Value = 0;
            MaintenanceDueBox.Value = double.NaN;
            NotesBox.Text = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        FlowDirection = loc.CurrentFlowDirection;
        ResourcesTitle.Text = loc.Text("الأسطول والمحاكيات", "Fleet & Simulators");
        ResourcesDescription.Text = loc.Text("إدارة جاهزية الطائرات وأجهزة المحاكاة، ساعات العدادات وحدود الصيانة.", "Manage aircraft and simulator readiness, meter hours, and maintenance thresholds.");
        SaveResourceTitle.Text = loc.Text("إضافة أو تحديث مورد", "Add or update a resource");
        RegistrationBox.PlaceholderText = loc.Text("رقم التسجيل / رمز المحاكي", "Registration / simulator ID");
        AircraftTypeBox.PlaceholderText = loc.Text("الطراز (مثال C172)", "Aircraft type (e.g. C172)");
        BaseBox.PlaceholderText = loc.Text("القاعدة / المطار", "Base / airport");
        HobbsBox.PlaceholderText = loc.Text("ساعات عداد هوبس", "Hobbs hours");
        TachBox.PlaceholderText = loc.Text("ساعات عداد تاك", "Tach hours");
        MaintenanceDueBox.PlaceholderText = loc.Text("حد الصيانة بالساعات", "Maintenance due at hours");
        NotesBox.PlaceholderText = loc.Text("ملاحظات / عطل مفتوح (اختياري)", "Notes / open defect (optional)");
        AircraftTypeItem.Content = loc.Text("طائرة", "Aircraft");
        SimulatorTypeItem.Content = loc.Text("جهاز محاكاة", "Simulator");
        StatusCombo.Items.Clear();
        StatusCombo.Items.Add(new ComboBoxItem { Content = loc.Text("متاحة", "Available"), Tag = "Available" });
        StatusCombo.Items.Add(new ComboBoxItem { Content = loc.Text("تحت الصيانة", "In maintenance"), Tag = "Maintenance" });
        StatusCombo.Items.Add(new ComboBoxItem { Content = loc.Text("غير صالحة للتشغيل", "Unserviceable"), Tag = "Unserviceable" });
        StatusCombo.SelectedIndex = 0;
        SaveResourceButtonText.Text = loc.Text("حفظ المورد", "Save resource");
    }
}
