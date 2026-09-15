using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Views;

public sealed partial class CompliancePage : Page
{
    private readonly ObservableCollection<Student> _students = new();
    private readonly ObservableCollection<ComplianceRecord> _records = new();
    public CompliancePage()
    {
        InitializeComponent();
        StudentCombo.ItemsSource = _students;
        RecordsList.ItemsSource = _records;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        Loaded += async (_, _) => { ApplyLocalization(); await LoadAsync(); };
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            await App.DatabaseService.InitializeAsync();
            var students = await App.DatabaseService.GetAllStudentsAsync();
            _students.Clear(); foreach (var student in students) _students.Add(student);
            var records = await App.DatabaseService.GetComplianceRecordsAsync();
            _records.Clear(); foreach (var record in records) _records.Add(record);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (StudentCombo.SelectedItem is not Student student) { ShowError(LocalizationService.Instance.Text("يرجى اختيار المتدرب.", "Select a trainee.")); return; }
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            await App.DatabaseService.SaveComplianceRecordAsync(new ComplianceRecord
            {
                StudentId = student.Id,
                RecordType = (RecordTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Medical",
                ReferenceNumber = ReferenceBox.Text,
                IssuedAt = IssuedPicker.Date?.DateTime,
                ExpiresAt = ExpiryPicker.Date?.DateTime,
                IsVerified = VerifiedCheckBox.IsChecked == true,
                Notes = NotesBox.Text
            });
            ReferenceBox.Text = string.Empty; NotesBox.Text = string.Empty; VerifiedCheckBox.IsChecked = false; ExpiryPicker.Date = null;
            await LoadAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        FlowDirection = loc.CurrentFlowDirection;
        TitleText.Text = loc.Text("الالتزام والصلاحيات", "Compliance & Validity");
        DescriptionText.Text = loc.Text("متابعة الفحوص الطبية، مستوى اللغة الإنجليزية، الرخص والوثائق المنتهية قبل الجدولة.", "Track medicals, English proficiency, licences, and expiring documents before scheduling.");
        AddTitleText.Text = loc.Text("إضافة سجل التزام", "Add compliance record");
        StudentCombo.PlaceholderText = loc.Text("المتدرب", "Trainee");
        MedicalItem.Content = loc.Text("كشف طبي", "Medical"); ElpItem.Content = "ICAO ELP"; LicenceItem.Content = loc.Text("رخصة", "Licence"); PassportItem.Content = loc.Text("جواز سفر", "Passport");
        ReferenceBox.PlaceholderText = loc.Text("رقم المرجع / الشهادة", "Reference / certificate number");
        VerifiedCheckBox.Content = loc.Text("تم التحقق", "Verified");
        IssuedPicker.PlaceholderText = loc.Text("تاريخ الإصدار", "Issue date"); ExpiryPicker.PlaceholderText = loc.Text("تاريخ الانتهاء", "Expiry date");
        NotesBox.PlaceholderText = loc.Text("ملاحظات أو قيود (اختياري)", "Notes or restrictions (optional)");
        HintText.Text = loc.Text("السجلات المعتمدة والمنتهية تمنع جدولة المتدرب حتى التجديد.", "Verified expired records block the trainee from scheduling until renewed.");
        SaveText.Text = loc.Text("حفظ السجل", "Save record");
    }

    private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
}
