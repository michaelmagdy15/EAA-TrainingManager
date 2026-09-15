using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Views;

public sealed partial class SchedulePage : Page
{
    private readonly ObservableCollection<TrainingSession> _sessions = new();
    private readonly ObservableCollection<Student> _students = new();

    public SchedulePage()
    {
        InitializeComponent();
        SessionsList.ItemsSource = _sessions;
        StudentCombo.ItemsSource = _students;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        Loaded += async (_, _) =>
        {
            ApplyLocalization();
            PickerDate.Date = DateTimeOffset.Now.Date;
            StartTimePicker.Time = new TimeSpan(8, 0, 0);
            EndTimePicker.Time = new TimeSpan(10, 0, 0);
            await LoadAsync();
        };
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            await App.DatabaseService.InitializeAsync();
            var students = await App.DatabaseService.GetAllStudentsAsync();
            _students.Clear();
            foreach (var student in students) _students.Add(student);
            var day = SelectedDate;
            var sessions = await App.DatabaseService.GetTrainingSessionsAsync(day);
            _sessions.Clear();
            foreach (var session in sessions) _sessions.Add(session);
            var loc = LocalizationService.Instance;
            ScheduleCountText.Text = loc.Text($"{_sessions.Count} جلسة — {day:yyyy/MM/dd}", $"{_sessions.Count} sessions — {day:yyyy/MM/dd}");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private DateTime SelectedDate => PickerDate.Date?.DateTime.Date ?? DateTime.Today;

    private async void PickerDate_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => await LoadAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void AddSession_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (StudentCombo.SelectedItem is not Student student)
        {
            ShowError("يرجى اختيار المتدرب.");
            return;
        }

        try
        {
            string track = (TrackCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Part61";
            var start = SelectedDate.Add(StartTimePicker.Time);
            var end = SelectedDate.Add(EndTimePicker.Time);
            await App.DatabaseService.ScheduleTrainingSessionAsync(new TrainingSession
            {
                StudentId = student.Id,
                RegulatoryTrack = track,
                LessonTitle = LessonBox.Text,
                InstructorName = InstructorBox.Text,
                ResourceName = ResourceBox.Text,
                Location = LocationBox.Text,
                StartAt = start,
                EndAt = end,
                Notes = NotesBox.Text
            });
            LessonBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void Dispatch_Click(object sender, RoutedEventArgs e) => await UpdateStatusAsync(sender, "Dispatched");
    private async void Complete_Click(object sender, RoutedEventArgs e) => await UpdateStatusAsync(sender, "Completed");
    private async void Cancel_Click(object sender, RoutedEventArgs e) => await UpdateStatusAsync(sender, "Cancelled");

    private async System.Threading.Tasks.Task UpdateStatusAsync(object sender, string status)
    {
        if (sender is Button { Tag: TrainingSession session })
        {
            await App.DatabaseService.UpdateTrainingSessionStatusAsync(session.Id, status);
            await LoadAsync();
        }
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
        ScheduleTitle.Text = loc.Text("التخطيط والتشغيل اليومي", "Daily Operations Scheduling");
        ScheduleDescription.Text = loc.Text("جدولة الدروس والرحلات مع منع تعارض المتدرب أو المدرب أو الطائرة / جهاز المحاكاة.", "Schedule lessons and flights while preventing trainee, instructor, and aircraft/simulator conflicts.");
        AddSessionTitle.Text = loc.Text("إضافة جلسة تدريب", "Add Training Session");
        StudentCombo.PlaceholderText = loc.Text("المتدرب", "Trainee");
        LessonBox.PlaceholderText = loc.Text("الدرس / النشاط", "Lesson / Activity");
        InstructorBox.PlaceholderText = loc.Text("المدرب", "Instructor");
        ResourceBox.PlaceholderText = loc.Text("الطائرة أو المحاكي", "Aircraft or simulator");
        LocationBox.PlaceholderText = loc.Text("الموقع / القاعة", "Location / classroom");
        NotesBox.PlaceholderText = loc.Text("ملاحظات التشغيل (اختياري)", "Operational notes (optional)");
        TrackPart61.Content = loc.Text("النظام الحر 61", "Part 61");
        TrackPart141.Content = loc.Text("النظام المعتمد 141", "Part 141");
        TrackETP.Content = loc.Text("الخط الجوي ETP", "ETP / Route");
        TrackTypeRating.Content = loc.Text("تأهيل طراز", "Type Rating");
        AddSessionButtonText.Text = loc.Text("إضافة للجدول", "Add to schedule");
    }
}
