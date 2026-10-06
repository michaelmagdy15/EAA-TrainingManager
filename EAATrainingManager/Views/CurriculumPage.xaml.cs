using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Views;

public sealed class CurriculumVersionOption
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed partial class CurriculumPage : Page
{
    private readonly ObservableCollection<CurriculumVersionOption> _versionOptions = new();
    private readonly ObservableCollection<CurriculumLesson> _lessons = new();
    private readonly ObservableCollection<TrainingObjective> _objectives = new();
    private readonly Dictionary<int, List<string>> _prerequisiteCodes = new();

    public CurriculumPage()
    {
        InitializeComponent();
        VersionCombo.ItemsSource = _versionOptions;
        LessonsList.ItemsSource = _lessons;
        ObjectivesList.ItemsSource = _objectives;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        Loaded += async (_, _) => { ApplyLocalization(); await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        try
        {
            await App.DatabaseService.InitializeAsync();
            await App.CurriculumService.EnsureStreamCatalogAsync();
            var versions = await App.CurriculumService.GetCurriculumVersionsAsync();
            _versionOptions.Clear();
            foreach (var version in versions)
            {
                _versionOptions.Add(new CurriculumVersionOption
                {
                    Id = version.Id,
                    Label = $"{version.VersionLabel} — {version.EffectiveFrom:yyyy-MM-dd} — {version.Status}"
                });
            }
            if (_versionOptions.Count > 0)
            {
                VersionCombo.SelectedIndex = 0;
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionCombo.SelectedItem is not CurriculumVersionOption option) return;
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            _objectives.Clear();
            _prerequisiteCodes.Clear();
            var lessons = await App.CurriculumService.GetLessonsForVersionAsync(option.Id);
            _lessons.Clear();
            foreach (var lesson in lessons) _lessons.Add(lesson);
            var prereq = await App.CurriculumService.GetPrerequisiteCodesForVersionAsync(option.Id);
            foreach (var pair in prereq) _prerequisiteCodes[pair.Key] = pair.Value;
            if (_lessons.Count > 0)
            {
                LessonsList.SelectedIndex = 0;
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void LessonsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LessonsList.SelectedItem is not CurriculumLesson lesson) return;
        try
        {
            _objectives.Clear();
            var loc = LocalizationService.Instance;
            var objectives = await App.CurriculumService.GetObjectivesForLessonAsync(lesson.Id);
            foreach (var objective in objectives)
            {
                if (_prerequisiteCodes.TryGetValue(objective.Id, out var codes))
                {
                    objective.PrerequisiteCodes = loc.Text("◀ المتطلبات الأساسية: ", "◀ Prerequisites: ") + string.Join("، ", codes);
                }
                _objectives.Add(objective);
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        FlowDirection = loc.CurrentFlowDirection;
        TitleText.Text = loc.Text("المناهج والخطط الدراسية", "Curriculum & Syllabi");
        DescriptionText.Text = loc.Text("استعراض نسخ المناهج، الدروس، الأهداف التدريبية ومعايير الإنجاز المعتمدة.", "Browse approved curriculum versions, lessons, training objectives and completion standards.");
        VersionCombo.PlaceholderText = loc.Text("اختر نسخة المنهج", "Select curriculum version");
        LessonsTitle.Text = loc.Text("دروس المنهج", "Curriculum lessons");
        ObjectivesTitle.Text = loc.Text("الأهداف التدريبية", "Training objectives");
    }

    private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
}