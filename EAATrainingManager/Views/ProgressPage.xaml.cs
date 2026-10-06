using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Helpers;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Views;

public sealed partial class ProgressPage : Page
{
    private readonly ObservableCollection<Student> _students = new();
    private readonly ObservableCollection<TrainingOrder> _orders = new();
    private readonly ObservableCollection<RemainingRequirement> _requirements = new();
    private readonly ObservableCollection<StageCheck> _stageChecks = new();
    private readonly ObservableCollection<RemedialPlan> _remedials = new();
    private readonly ObservableCollection<TrainingHourIssue> _issues = new();
    private TrainingHourReconciliation? _reconciliation;

    public ProgressPage()
    {
        InitializeComponent();
        StudentCombo.ItemsSource = _students;
        OrderCombo.ItemsSource = _orders;
        RequirementsList.ItemsSource = _requirements;
        StageChecksList.ItemsSource = _stageChecks;
        RemedialsList.ItemsSource = _remedials;
        IssuesList.ItemsSource = _issues;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        Loaded += async (_, _) => { ApplyLocalization(); await LoadAsync(); };
    }

    private string? SessionId => App.IdentityService.CurrentSession?.SessionId;

    private async Task LoadAsync()
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            SuccessText.Visibility = Visibility.Collapsed;
            if (SessionId == null)
            {
                ShowError(LocalizationService.Instance.Text("لا توجد جلسة نشطة.", "No active session."));
                return;
            }
            await App.DatabaseService.InitializeAsync();
            var students = await App.DatabaseService.GetAllStudentsAsync();
            _students.Clear();
            foreach (var student in students) _students.Add(student);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void StudentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StudentCombo.SelectedItem is not Student student) return;
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            _orders.Clear();
            _requirements.Clear();
            _stageChecks.Clear();
            _remedials.Clear();
            _issues.Clear();
            ExportButton.IsEnabled = false;
            AcknowledgeButton.IsEnabled = false;
            var orders = await App.DatabaseService.GetAllOrdersAsync();
            foreach (var order in orders.Where(o => o.StudentId == student.Id))
            {
                _orders.Add(order);
            }
            if (_orders.Count > 0) OrderCombo.SelectedIndex = 0;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void OrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OrderCombo.SelectedItem is not TrainingOrder order) return;
        if (StudentCombo.SelectedItem is not Student student) return;
        await LoadOrderAsync(student.Id, order.Id);
    }

    private async Task LoadOrderAsync(int studentId, int trainingOrderId)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            SuccessText.Visibility = Visibility.Collapsed;
            if (SessionId == null) { ShowError(LocalizationService.Instance.Text("لا توجد جلسة نشطة.", "No active session.")); return; }

            var loc = LocalizationService.Instance;
            var requirements = await App.TrainingRecordService.GetRemainingRequirementsAsync(studentId, trainingOrderId);
            _requirements.Clear();
            foreach (var requirement in requirements) _requirements.Add(requirement);

            var stageChecks = await App.TrainingRecordService.GetStageChecksAsync(studentId, trainingOrderId);
            _stageChecks.Clear();
            foreach (var check in stageChecks) _stageChecks.Add(check);

            var remedials = await App.TrainingRecordService.GetRemedialPlansAsync(studentId, trainingOrderId);
            var record = await App.TrainingRecordService.GetOfficialTrainingRecordAsync(studentId, trainingOrderId);
            var objectiveCodes = new Dictionary<int, string>();
            foreach (var lesson in record.Lessons)
                foreach (var objective in lesson.Objectives)
                    objectiveCodes[objective.ObjectiveId] = objective.ObjectiveCode;
            _remedials.Clear();
            foreach (var plan in remedials)
            {
                if (objectiveCodes.TryGetValue(plan.ObjectiveId, out var code)) plan.ObjectiveCode = code;
                _remedials.Add(plan);
            }

            _reconciliation = await App.TrainingRecordService.ReconcileTrainingHoursAsync(studentId, trainingOrderId);
            HoursRequiredValue.Text = _reconciliation.RequiredHours.ToString("0.##");
            HoursScheduledValue.Text = _reconciliation.ScheduledSessionHours.ToString("0.##");
            HoursCompletedValue.Text = _reconciliation.CompletedSessionHours.ToString("0.##");
            HoursFlightValue.Text = _reconciliation.FlightRecordHours.ToString("0.##");
            HoursHobbsValue.Text = _reconciliation.HobbsRecordedHours.ToString("0.##");
            HoursRemainingValue.Text = _reconciliation.RemainingRequiredHours.ToString("0.##");
            ReconcileText.Text = _reconciliation.IsReconciled
                ? loc.Text("ساعات التدريب مطابقة ✓", "Training hours reconciled ✓")
                : loc.Text($"توجد فروقات (متبقٍ {_reconciliation.RemainingRequiredHours:0.##} ساعة)", $"Variances found ({_reconciliation.RemainingRequiredHours:0.##} hours remaining)");
            (ReconcileText.Parent as Border)!.Background =
                _reconciliation.IsReconciled
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { R = 25, G = 135, B = 84, A = 255 })
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(new Windows.UI.Color { R = 220, G = 53, B = 69, A = 255 });
            _issues.Clear();
            foreach (var issue in _reconciliation.Issues) _issues.Add(issue);
            (IssuesList.Parent as Border)!.Visibility = _reconciliation.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            ExportButton.IsEnabled = true;
            AcknowledgeButton.IsEnabled = true;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (StudentCombo.SelectedItem is not Student student) return;
        if (OrderCombo.SelectedItem is not TrainingOrder order) return;
        if (SessionId == null) { ShowError(LocalizationService.Instance.Text("لا توجد جلسة نشطة.", "No active session.")); return; }
        try
        {
            var loc = LocalizationService.Instance;
            var path = await FilePickerHelper.PickSaveExcelPathAsync($"OfficialTrainingRecord_{order.OrderNumber}.xlsx");
            if (string.IsNullOrWhiteSpace(path)) return;
            await App.TrainingRecordService.ExportOfficialTrainingRecordAsync(path, student.Id, order.Id);
            ShowSuccess(loc.Text($"تم تصدير السجل الرسمي إلى: {path}", $"Official training record exported to: {path}"));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Acknowledge_Click(object sender, RoutedEventArgs e)
    {
        if (StudentCombo.SelectedItem is not Student student) return;
        if (OrderCombo.SelectedItem is not TrainingOrder order) return;
        if (SessionId == null) { ShowError(LocalizationService.Instance.Text("لا توجد جلسة نشطة.", "No active session.")); return; }

        var loc = LocalizationService.Instance;
        var reasonBox = new TextBox { PlaceholderText = loc.Text("سبب التوقيع (8 أحرف على الأقل)", "Signature reason (at least 8 characters)"), MinWidth = 320, Margin = new Thickness(0, 0, 0, 12) };
        var passwordBox = new PasswordBox { PlaceholderText = loc.Text("كلمة المرور", "Password"), MinWidth = 320 };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = loc.Text("توقيع المتدرب: يتم عبر جلسة حساب المتدرب.", "Trainee signature: performed through the trainee account session."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(reasonBox);
        panel.Children.Add(passwordBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = loc.Text("توقيع المتدرب على السجل الرسمي", "Trainee acknowledgment of the official record"),
            Content = panel,
            PrimaryButtonText = loc.Text("توقيع", "Sign"),
            CloseButtonText = loc.Text("إلغاء", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await App.TrainingRecordService.AcknowledgeOfficialTrainingRecordAsync(SessionId, student.Id, order.Id, reasonBox.Text, passwordBox.Password);
            ShowSuccess(loc.Text("تم التوقيع: ذكر المتدرب استلامه للمستند الرسمي رسميًا.", "Signed: the trainee officially acknowledged receipt of the record."));
            await LoadOrderAsync(student.Id, order.Id);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        FlowDirection = loc.CurrentFlowDirection;
        TitleText.Text = loc.Text("بوابة تقدم المتدرب", "Trainee Progress Gateway");
        DescriptionText.Text = loc.Text("المتطلبات المتبقية، العوائق، الفحوصات المرحلية، الخطط العلاجية ومطابقة الساعات.", "Remaining requirements, blockers, stage checks, remedial plans and hour reconciliation.");
        StudentCombo.PlaceholderText = loc.Text("اختر المتدرب", "Select trainee");
        OrderCombo.PlaceholderText = loc.Text("اختر أمر التدريب", "Select training order");
        HoursRequiredLabel.Text = loc.Text("الساعات المطلوبة", "Required hours");
        HoursScheduledLabel.Text = loc.Text("ساعات مجدولة", "Scheduled");
        HoursCompletedLabel.Text = loc.Text("ساعات مكتملة", "Completed");
        HoursFlightLabel.Text = loc.Text("ساعات سجلات الطيران", "Flight records");
        HoursHobbsLabel.Text = loc.Text("ساعات الهوبس", "Hobbs hours");
        HoursRemainingLabel.Text = loc.Text("الساعات المتبقية", "Remaining hours");
        RequirementsTitle.Text = loc.Text("المتطلبات المتبقية", "Remaining requirements");
        StageChecksTitle.Text = loc.Text("الفحوصات المرحلية", "Stage checks");
        RemedialsTitle.Text = loc.Text("الخطط العلاجية", "Remedial plans");
        ExportText.Text = loc.Text("تصدير السجل الرسمي (Excel)", "Export official record (Excel)");
        AcknowledgeText.Text = loc.Text("توقيع المتدرب", "Trainee signature");
    }

    private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; SuccessText.Visibility = Visibility.Collapsed; }
    private void ShowSuccess(string message) { SuccessText.Text = message; SuccessText.Visibility = Visibility.Visible; ErrorText.Visibility = Visibility.Collapsed; }
}