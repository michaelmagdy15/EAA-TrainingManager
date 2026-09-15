using System;
using System.Collections.ObjectModel;
using System.Globalization;
using EAATrainingManager.Models;
using EAATrainingManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EAATrainingManager.Views;

public sealed partial class FlightRecordsPage : Page
{
    private readonly ObservableCollection<Student> _students = new();
    private readonly ObservableCollection<AircraftResource> _resources = new();
    private readonly ObservableCollection<FlightRecord> _records = new();
    public FlightRecordsPage()
    {
        InitializeComponent(); StudentCombo.ItemsSource = _students; ResourceCombo.ItemsSource = _resources; RecordsList.ItemsSource = _records;
        DatePicker.Date = DateTimeOffset.Now; StartTimePicker.Time = DateTime.Now.TimeOfDay; EndTimePicker.Time = DateTime.Now.AddHours(1).TimeOfDay;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        Loaded += async (_, _) => { ApplyLocalization(); await LoadAsync(); };
        Unloaded += (_, _) => LocalizationService.Instance.LanguageChanged -= ApplyLocalization;
    }
    private async System.Threading.Tasks.Task LoadAsync()
    {
        try { await App.DatabaseService.InitializeAsync(); var students = await App.DatabaseService.GetAllStudentsAsync(); var resources = await App.DatabaseService.GetAircraftResourcesAsync(); var records = await App.DatabaseService.GetFlightRecordsAsync(); _students.Clear(); foreach (var x in students) _students.Add(x); _resources.Clear(); foreach (var x in resources) _resources.Add(x); _records.Clear(); foreach (var x in records) _records.Add(x); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (StudentCombo.SelectedItem is not Student student) { ShowError(LocalizationService.Instance.Text("يرجى اختيار المتدرب.", "Select a trainee.")); return; }
        if (!TryReadNumber(HobbsStartBox.Text, out var startHobbs) || !TryReadNumber(HobbsEndBox.Text, out var endHobbs)) { ShowError(LocalizationService.Instance.Text("أدخل قراءات Hobbs رقمية للبداية والنهاية.", "Enter numeric Hobbs start and end readings.")); return; }
        try
        {
            ErrorText.Visibility = Visibility.Collapsed; var day = DatePicker.Date?.DateTime.Date ?? DateTime.Today;
            await App.DatabaseService.RecordFlightAsync(new FlightRecord { StudentId = student.Id, ActivityType = (ActivityTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Dual", ResourceName = (ResourceCombo.SelectedItem as AircraftResource)?.Registration ?? string.Empty, InstructorName = InstructorBox.Text, Route = RouteBox.Text, StartAt = day + StartTimePicker.Time, EndAt = day + EndTimePicker.Time, HobbsStart = startHobbs, HobbsEnd = endHobbs, Landings = (int)LandingsBox.Value, Remarks = RemarksBox.Text });
            InstructorBox.Text = string.Empty; RouteBox.Text = string.Empty; RemarksBox.Text = string.Empty; HobbsStartBox.Text = string.Empty; HobbsEndBox.Text = string.Empty; LandingsBox.Value = 0; await LoadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private static bool TryReadNumber(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance; FlowDirection = loc.CurrentFlowDirection; TitleText.Text = loc.Text("سجل الطيران والدروس", "Flight & Lesson Records"); DescriptionText.Text = loc.Text("توثيق الطلعات الجوية والمحاكيات والدروس المكتملة مع الزمن والهبوط وساعات الطائرة.", "Record completed flights, simulator sorties, and lessons with time, landings, and aircraft hours."); AddTitleText.Text = loc.Text("إضافة سجل تشغيل مكتمل", "Add completed operational record"); StudentCombo.PlaceholderText = loc.Text("المتدرب", "Trainee"); ResourceCombo.PlaceholderText = loc.Text("الطائرة / المحاكي", "Aircraft / simulator"); InstructorBox.PlaceholderText = loc.Text("المدرب", "Instructor"); DatePicker.PlaceholderText = loc.Text("التاريخ", "Date"); RouteBox.PlaceholderText = loc.Text("المسار / عنوان الدرس", "Route / lesson title"); HobbsStartBox.PlaceholderText = loc.Text("Hobbs بداية", "Hobbs start"); HobbsEndBox.PlaceholderText = loc.Text("Hobbs نهاية", "Hobbs end"); LandingsBox.Header = loc.Text("عدد الهبوط", "Landings"); RemarksBox.PlaceholderText = loc.Text("ملاحظات (اختياري)", "Remarks (optional)"); DualItem.Content = loc.Text("طيران مزدوج", "Dual"); SoloItem.Content = loc.Text("طيران فردي", "Solo"); PicItem.Content = "PIC"; SimulatorItem.Content = loc.Text("محاكي", "Simulator"); GroundItem.Content = loc.Text("تدريب أرضي", "Ground"); HintText.Text = loc.Text("يحفظ السجل كدليل تشغيلي ويحدّث ساعات Hobbs للطائرة المسجلة.", "The record becomes operational evidence and updates Hobbs hours for the selected aircraft."); SaveText.Text = loc.Text("حفظ السجل", "Save record");
    }
    private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
}
