using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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
            ViewModePicker.SelectedIndex = 0;
            StartTimePicker.Time = new TimeSpan(8, 0, 0);
            EndTimePicker.Time = new TimeSpan(10, 0, 0);
            UserSession? session = App.IdentityService.CurrentSession;
            bool canManageSchedule = session != null && await App.IdentityService.HasPermissionAsync(session.SessionId, "schedule", PermissionLevel.FullEdit);
            bool canManageResources = session != null && await App.IdentityService.HasPermissionAsync(session.SessionId, "resources", PermissionLevel.FullEdit);
            AvailabilityButton.Visibility = canManageSchedule || canManageResources ? Visibility.Visible : Visibility.Collapsed;
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
            DateTime rangeStart = day.Date;
            DateTime rangeEnd = rangeStart.AddDays(1);
            List<TrainingSession> sessions;
            if ((ViewModePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Week")
            {
                DateTime firstDay = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                rangeStart = firstDay;
                rangeEnd = firstDay.AddDays(7);
                sessions = new List<TrainingSession>();
                for (int offset = 0; offset < 7; offset++)
                    sessions.AddRange(await App.DatabaseService.GetTrainingSessionsAsync(firstDay.AddDays(offset)));
                sessions = sessions.OrderBy(s => s.StartAt).ToList();
            }
            else
            {
                sessions = await App.DatabaseService.GetTrainingSessionsAsync(day);
            }
            _sessions.Clear();
            foreach (var session in sessions) _sessions.Add(session);
            var loc = LocalizationService.Instance;
            DateTime periodStart = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
            string periodLabel = (ViewModePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Week"
                ? $"{periodStart:yyyy/MM/dd} — {periodStart.AddDays(6):yyyy/MM/dd}"
                : day.ToString("yyyy/MM/dd");
            ScheduleCountText.Text = loc.Text($"{_sessions.Count} جلسة — {periodLabel}", $"{_sessions.Count} sessions — {periodLabel}");
            var operations = await App.DatabaseService.GetFlightOperationsMetricsAsync(rangeStart, rangeEnd);
            string exceptionReasons = string.Join(" | ", operations.CancellationReasons.Take(3));
            string utilization = string.Join(", ", operations.HoursByResource.Select(entry => $"{entry.Key}: {entry.Value:F1}h"));
            OperationsMetricsText.Text = loc.Text(
                $"مجدولة {operations.Scheduled} | مؤكدة {operations.Confirmed} | تصاريح {operations.Released} | في الجو {operations.Airborne} | هبوط {operations.Landed} | مكتملة {operations.Completed} | ملغاة {operations.Cancelled} | لم يحضر {operations.NoShow} | ساعات {operations.CompletedFlightHours:F1} | الأسطول: {utilization} | الأسباب: {exceptionReasons}",
                $"Scheduled {operations.Scheduled} | Confirmed {operations.Confirmed} | Released {operations.Released} | Airborne {operations.Airborne} | Landed {operations.Landed} | Completed {operations.Completed} | Cancelled {operations.Cancelled} | No-show {operations.NoShow} | Flight hours {operations.CompletedFlightHours:F1} | Fleet: {utilization} | Reasons: {exceptionReasons}");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private DateTime SelectedDate => PickerDate.Date?.DateTime.Date ?? DateTime.Today;

    private async void PickerDate_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => await LoadAsync();
    private async void ViewModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => await LoadAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Availability_Click(object sender, RoutedEventArgs e)
    {
        var resourceType = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in new[]
        {
            ("Student", "متدرب / Student"), ("Instructor", "مدرب / Instructor"),
            ("Resource", "طائرة أو محاكي / Aircraft or simulator"), ("Room", "قاعة / Room"),
            ("Weather", "قيد طقس يدوي / Weather restriction")
        })
            resourceType.Items.Add(new ComboBoxItem { Content = item.Item2, Tag = item.Item1 });
        resourceType.SelectedIndex = 0;
        var resourceName = new TextBox { PlaceholderText = LocalizationService.Instance.Text("اسم المورد أو رقم المتدرب", "Resource name or student ID") };
        var state = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        state.Items.Add(new ComboBoxItem { Content = LocalizationService.Instance.Text("غير متاح / إغلاق", "Unavailable / blackout"), Tag = "Unavailable" });
        state.Items.Add(new ComboBoxItem { Content = LocalizationService.Instance.Text("متاح ضمن هذه الفترة", "Available during this window"), Tag = "Available" });
        state.SelectedIndex = 0;
        var startTime = new TimePicker { ClockIdentifier = "24HourClock", Time = new TimeSpan(8, 0, 0) };
        var endTime = new TimePicker { ClockIdentifier = "24HourClock", Time = new TimeSpan(10, 0, 0) };
        var location = new TextBox { PlaceholderText = LocalizationService.Instance.Text("الموقع (اختياري)", "Location (optional)") };
        var reason = new TextBox { PlaceholderText = LocalizationService.Instance.Text("سبب الإغلاق أو التوافر", "Reason for blackout/availability"), TextWrapping = TextWrapping.Wrap };
        var windows = new ListView { DisplayMemberPath = nameof(AvailabilityWindow.DisplayText), MinHeight = 100, SelectionMode = ListViewSelectionMode.Single };
        var statusText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 8, MinWidth = 420 };
        content.Children.Add(new TextBlock { Text = LocalizationService.Instance.Text("الفترات المسجلة لهذا اليوم", "Windows recorded for this day"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(windows);
        content.Children.Add(resourceType);
        content.Children.Add(resourceName);
        content.Children.Add(state);
        content.Children.Add(startTime);
        content.Children.Add(endTime);
        content.Children.Add(location);
        content.Children.Add(reason);
        content.Children.Add(statusText);

        UserSession? actor = App.IdentityService.CurrentSession;
        bool canWriteSchedule = actor != null && await App.IdentityService.HasPermissionAsync(actor.SessionId, "schedule", PermissionLevel.FullEdit);
        bool canWriteResources = actor != null && await App.IdentityService.HasPermissionAsync(actor.SessionId, "resources", PermissionLevel.FullEdit);
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Instance.Text("إدارة فترات التوافر والإغلاق", "Availability and blackout windows"),
            Content = content,
            PrimaryButtonText = canWriteSchedule || canWriteResources ? LocalizationService.Instance.Text("حفظ الفترة", "Save window") : string.Empty,
            SecondaryButtonText = canWriteSchedule || canWriteResources ? LocalizationService.Instance.Text("أرشفة المحدد", "Archive selected") : string.Empty,
            CloseButtonText = LocalizationService.Instance.Text("إغلاق", "Close"),
            XamlRoot = XamlRoot
        };

        async Task RefreshWindowsAsync()
        {
            windows.ItemsSource = await App.DatabaseService.GetAvailabilityWindowsAsync(SelectedDate);
        }
        await RefreshWindowsAsync();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (!(canWriteSchedule || canWriteResources)) return;
            var deferral = args.GetDeferral();
            try
            {
                await App.DatabaseService.SaveAvailabilityWindowAsync(new AvailabilityWindow
                {
                    ResourceType = (resourceType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Resource",
                    ResourceName = resourceName.Text,
                    AvailabilityState = (state.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Unavailable",
                    StartAt = SelectedDate.Add(startTime.Time),
                    EndAt = SelectedDate.Add(endTime.Time),
                    Location = location.Text,
                    Reason = reason.Text
                });
                statusText.Text = LocalizationService.Instance.Text("تم حفظ الفترة.", "Availability window saved.");
                await RefreshWindowsAsync();
                args.Cancel = true;
            }
            catch (Exception ex)
            {
                AppLogService.LogException("Schedule.Availability.Save", ex, "AvailabilityWindow", resourceName.Text.Trim());
                statusText.Text = LocalizationService.Instance.Text("تعذر حفظ الفترة. تحقق من الصلاحية والبيانات.", "Could not save the window. Check permissions and input.");
                args.Cancel = true;
            }
            finally { deferral.Complete(); }
        };
        dialog.SecondaryButtonClick += async (_, args) =>
        {
            if (!(canWriteSchedule || canWriteResources) || windows.SelectedItem is not AvailabilityWindow selectedWindow) return;
            var deferral = args.GetDeferral();
            try
            {
                await App.DatabaseService.ArchiveAvailabilityWindowAsync(selectedWindow.Id);
                await RefreshWindowsAsync();
                statusText.Text = LocalizationService.Instance.Text("تمت أرشفة الفترة.", "Availability window archived.");
                args.Cancel = true;
            }
            catch (Exception ex)
            {
                AppLogService.LogException("Schedule.Availability.Archive", ex, "AvailabilityWindow", selectedWindow.Id.ToString());
                statusText.Text = LocalizationService.Instance.Text("تعذر أرشفة الفترة.", "Could not archive the window.");
                args.Cancel = true;
            }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
    }

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

    private async void Dispatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrainingSession session }) return;
        try
        {
            switch (session.Status)
            {
                case "Scheduled":
                    await App.FlightOperationsService.TransitionAsync(session.Id, "Confirmed");
                    break;
                case "Confirmed":
                    if (!await ShowDispatchReleaseDialogAsync(session)) return;
                    break;
                case "Released":
                case "Dispatched":
                    await App.FlightOperationsService.TransitionAsync(session.Id, "Airborne");
                    break;
                case "Airborne":
                    await App.FlightOperationsService.TransitionAsync(session.Id, "Landed");
                    break;
                default:
                    return;
            }
            await LoadAsync();
        }
        catch (Exception ex) { ShowError(LocalizationService.Instance.Text("تعذر تحديث حالة التشغيل.", "Could not update dispatch state.")); AppLogService.LogException("DispatchBoard.AdvanceState", ex, "TrainingSession", session.Id.ToString()); }
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrainingSession session }) return;
        if (session.Status != "Landed")
        {
            ShowError(LocalizationService.Instance.Text("يجب تسجيل الهبوط قبل إتمام سجل الرحلة.", "Record landing before completing the flight record."));
            return;
        }
        if (await ShowPostFlightDialogAsync(session))
            await LoadAsync();
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e) => await ResolveSessionAsync(sender, "Cancelled");
    private async void NoShow_Click(object sender, RoutedEventArgs e) => await ResolveSessionAsync(sender, "NoShow");

    private async void Reschedule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrainingSession session }) return;
        var datePicker = new CalendarDatePicker { Date = new DateTimeOffset(session.StartAt.Date) };
        var startTime = new TimePicker { ClockIdentifier = "24HourClock", Time = session.StartAt.TimeOfDay };
        var endTime = new TimePicker { ClockIdentifier = "24HourClock", Time = session.EndAt.TimeOfDay };
        var instructor = new TextBox { Text = session.InstructorName, PlaceholderText = LocalizationService.Instance.Text("المدرب", "Instructor") };
        var resource = new TextBox { Text = session.ResourceName, PlaceholderText = LocalizationService.Instance.Text("المورد", "Resource") };
        var location = new TextBox { Text = session.Location, PlaceholderText = LocalizationService.Instance.Text("الموقع / القاعة", "Location / room") };
        var reason = new TextBox { PlaceholderText = LocalizationService.Instance.Text("سبب إعادة الجدولة", "Reschedule reason"), TextWrapping = TextWrapping.Wrap, MinHeight = 56 };
        var error = new TextBlock { Visibility = Visibility.Collapsed, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed), TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 8, MinWidth = 400 };
        content.Children.Add(new TextBlock { Text = LocalizationService.Instance.Text($"إعادة جدولة الجلسة رقم {session.Id} في {session.StartAt:yyyy/MM/dd}", $"Reschedule session #{session.Id} on {session.StartAt:yyyy/MM/dd}") });
        content.Children.Add(datePicker); content.Children.Add(startTime); content.Children.Add(endTime); content.Children.Add(instructor); content.Children.Add(resource); content.Children.Add(location); content.Children.Add(reason); content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Instance.Text("إعادة جدولة النشاط", "Reschedule activity"),
            Content = content,
            PrimaryButtonText = LocalizationService.Instance.Text("حفظ التعديل", "Save change"),
            CloseButtonText = LocalizationService.Instance.Text("إلغاء", "Cancel"),
            XamlRoot = XamlRoot
        };
        bool saved = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                await App.DatabaseService.RescheduleTrainingSessionAsync(
                    session.Id,
                    (datePicker.Date?.DateTime.Date ?? session.StartAt.Date).Add(startTime.Time),
                    (datePicker.Date?.DateTime.Date ?? session.StartAt.Date).Add(endTime.Time),
                    instructor.Text,
                    resource.Text,
                    location.Text,
                    reason.Text);
                saved = true;
            }
            catch (Exception ex)
            {
                AppLogService.LogException("Schedule.Reschedule", ex, "TrainingSession", session.Id.ToString());
                error.Text = LocalizationService.Instance.Text("تعذر إعادة الجدولة. تحقق من السبب والتوافر والتعارضات.", "Rescheduling failed. Check the reason, availability, and conflicts.");
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
        if (saved) await LoadAsync();
    }

    private async Task ResolveSessionAsync(object sender, string outcome)
    {
        if (sender is not Button { Tag: TrainingSession session }) return;
        var reasonBox = new TextBox { PlaceholderText = LocalizationService.Instance.Text("سبب الإلغاء / عدم الحضور", "Cancellation / no-show reason"), TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        var dialog = new ContentDialog
        {
            Title = outcome == "NoShow" ? LocalizationService.Instance.Text("تسجيل عدم الحضور", "Record no-show") : LocalizationService.Instance.Text("إلغاء الحجز", "Cancel booking"),
            Content = reasonBox,
            PrimaryButtonText = LocalizationService.Instance.Text("حفظ السبب", "Save reason"),
            CloseButtonText = LocalizationService.Instance.Text("رجوع", "Back"),
            XamlRoot = XamlRoot
        };
        bool saved = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                await App.FlightOperationsService.ResolveAsync(session.Id, outcome, reasonBox.Text);
                saved = true;
            }
            catch (Exception ex)
            {
                AppLogService.LogException("DispatchBoard.Resolve", ex, "TrainingSession", session.Id.ToString());
                args.Cancel = true;
                ShowError(LocalizationService.Instance.Text("تعذر حفظ سبب الإلغاء.", "Could not save the cancellation reason."));
            }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
        if (saved) await LoadAsync();
    }

    private async Task<bool> ShowDispatchReleaseDialogAsync(TrainingSession session)
    {
        var pilotCheck = new CheckBox { Content = LocalizationService.Instance.Text("تم تأكيد هوية الطيار والطاقم", "Pilot/crew identity confirmed") };
        var weatherCheck = new CheckBox { Content = LocalizationService.Instance.Text("تمت مراجعة موجز الطقس التشغيلي", "Operational weather briefing reviewed") };
        var resourceCheck = new CheckBox { Content = LocalizationService.Instance.Text("تم تأكيد حالة المورد", "Resource status confirmed") };
        var fileCheck = new CheckBox { Content = LocalizationService.Instance.Text("تمت مراجعة ملف معلومات الرحلة", "Flight information file reviewed") };
        var weather = new TextBox { PlaceholderText = LocalizationService.Instance.Text("ملخص موجز الطقس", "Weather briefing summary") };
        var flightInfo = new TextBox { PlaceholderText = LocalizationService.Instance.Text("مرجع ملف معلومات الرحلة", "Flight information file reference") };
        var reason = new TextBox { PlaceholderText = LocalizationService.Instance.Text("سبب / ملاحظات التصريح", "Release reason / notes"), TextWrapping = TextWrapping.Wrap, MinHeight = 56 };
        var error = new TextBlock { Visibility = Visibility.Collapsed, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed), TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 7, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = LocalizationService.Instance.Text($"إصدار تصريح للجلسة #{session.Id}", $"Release session #{session.Id}"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(pilotCheck); panel.Children.Add(weatherCheck); panel.Children.Add(resourceCheck); panel.Children.Add(fileCheck);
        panel.Children.Add(weather); panel.Children.Add(flightInfo); panel.Children.Add(reason); panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Instance.Text("قائمة فحص تصريح التشغيل", "Dispatch release checklist"),
            Content = panel,
            PrimaryButtonText = LocalizationService.Instance.Text("إصدار التصريح", "Release flight"),
            CloseButtonText = LocalizationService.Instance.Text("إلغاء", "Cancel"),
            XamlRoot = XamlRoot
        };
        bool released = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var checks = new Dictionary<string, bool>
                {
                    ["PilotIdentityConfirmed"] = pilotCheck.IsChecked == true,
                    ["WeatherBriefingReviewed"] = weatherCheck.IsChecked == true,
                    ["ResourceStatusConfirmed"] = resourceCheck.IsChecked == true,
                    ["FlightInformationFileReviewed"] = fileCheck.IsChecked == true
                };
                await App.FlightOperationsService.ReleaseAsync(session.Id, new DispatchReleaseRequest(weather.Text, flightInfo.Text, reason.Text, checks));
                released = true;
            }
            catch (Exception ex)
            {
                AppLogService.LogException("DispatchBoard.Release", ex, "TrainingSession", session.Id.ToString());
                error.Text = LocalizationService.Instance.Text("تعذر إصدار التصريح. أكمل عناصر الفحص والبيانات المطلوبة.", "Release was blocked. Complete all checks and required information.");
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
        return released;
    }

    private async Task<bool> ShowPostFlightDialogAsync(TrainingSession session)
    {
        var hobbsStart = new TextBox { PlaceholderText = LocalizationService.Instance.Text("قراءة Hobbs عند البداية", "Starting Hobbs"), Text = "0" };
        var hobbsEnd = new TextBox { PlaceholderText = LocalizationService.Instance.Text("قراءة Hobbs عند النهاية", "Ending Hobbs"), Text = "0" };
        var landings = new TextBox { PlaceholderText = LocalizationService.Instance.Text("عدد مرات الهبوط", "Landing count"), Text = "0" };
        var route = new TextBox { PlaceholderText = LocalizationService.Instance.Text("المسار", "Route") };
        var remarks = new TextBox { PlaceholderText = LocalizationService.Instance.Text("ملاحظات ما بعد الرحلة", "Post-flight remarks"), TextWrapping = TextWrapping.Wrap, MinHeight = 56 };
        var error = new TextBlock { Visibility = Visibility.Collapsed, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed), TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 8, MinWidth = 400 };
        panel.Children.Add(new TextBlock { Text = LocalizationService.Instance.Text($"إتمام الرحلة للمتدرب {session.StudentDisplayName}", $"Complete flight for {session.StudentDisplayName}"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(hobbsStart); panel.Children.Add(hobbsEnd); panel.Children.Add(landings); panel.Children.Add(route); panel.Children.Add(remarks); panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Instance.Text("تقرير ما بعد الرحلة", "Post-flight debrief"),
            Content = panel,
            PrimaryButtonText = LocalizationService.Instance.Text("حفظ سجل الرحلة", "Save flight record"),
            CloseButtonText = LocalizationService.Instance.Text("إلغاء", "Cancel"),
            XamlRoot = XamlRoot
        };
        bool saved = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (!double.TryParse(hobbsStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hobbsStartValue) ||
                    !double.TryParse(hobbsEnd.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hobbsEndValue) ||
                    !int.TryParse(landings.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int landingCount))
                    throw new ArgumentException("Enter valid Hobbs readings and landing count.");

                await App.DatabaseService.RecordFlightAsync(new FlightRecord
                {
                    TrainingSessionId = session.Id,
                    StudentId = session.StudentId,
                    ActivityType = "Dual",
                    ResourceName = session.ResourceName,
                    InstructorName = session.InstructorName,
                    Route = route.Text,
                    StartAt = session.StartAt,
                    EndAt = session.EndAt,
                    HobbsStart = hobbsStartValue,
                    HobbsEnd = hobbsEndValue,
                    Landings = landingCount,
                    Remarks = remarks.Text
                });
                saved = true;
            }
            catch (Exception ex)
            {
                AppLogService.LogException("DispatchBoard.PostFlight", ex, "TrainingSession", session.Id.ToString());
                error.Text = LocalizationService.Instance.Text("تعذر حفظ سجل الرحلة. تحقق من القراءات والهبوطات.", "Could not save flight record. Check meter readings and landings.");
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
        return saved;
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
        DayViewItem.Content = loc.Text("يوم", "Day");
        WeekViewItem.Content = loc.Text("أسبوع", "Week");
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
        AvailabilityButton.Content = loc.Text("فترات التوافر", "Availability windows");
    }
}
