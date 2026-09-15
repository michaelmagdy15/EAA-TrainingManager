using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Dialogs;

public sealed partial class AddOrderDialog : ContentDialog
{
    private Student? _selectedExistingStudent = null;
    private readonly DatabaseService _databaseService;
    private readonly ExcelMirrorService? _excelMirrorService;
    private readonly BackupService? _backupService;

    public TrainingOrder? CreatedOrder { get; private set; }

    public AddOrderDialog(DatabaseService databaseService, ExcelMirrorService? mirrorService = null, BackupService? backupService = null)
    {
        _databaseService = databaseService;
        _excelMirrorService = mirrorService;
        _backupService = backupService;

        InitializeComponent();
        Loaded += AddOrderDialog_Loaded;
    }

    private void AddOrderDialog_Loaded(object sender, RoutedEventArgs e)
    {
        PickerEnrollmentDate.Date = DateTimeOffset.Now;
        UpdateStreamPanels("Part61");
    }

    private async void TxtStudentName_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            string query = sender.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                _selectedExistingStudent = null;
                TxtDedupStatus.Text = "سيتم إنشاء ملف جديد للطالب";
                TxtDedupStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.SeaGreen);
                sender.ItemsSource = null;
                return;
            }

            try
            {
                var matches = await _databaseService.SearchStudentsByNamePrefixAsync(query, 6);
                sender.ItemsSource = matches.Select(m => m.DisplayName).ToList();

                var exact = matches.FirstOrDefault(m => string.Equals(m.DisplayName, query, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    SelectStudent(exact);
                }
                else if (matches.Count > 0)
                {
                    TxtDedupStatus.Text = $"يوجد {matches.Count} متدرب بأسماء مشابهة في قاعدة البيانات (اختر من القائمة للربط)";
                    TxtDedupStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                }
                else
                {
                    _selectedExistingStudent = null;
                    TxtDedupStatus.Text = "✓ اسم جديد: سيتم إنشاء ملف تعريفي موحد للطالب";
                    TxtDedupStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.SeaGreen);
                }
            }
            catch { }
        }
    }

    private async void TxtStudentName_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        string chosenName = args.SelectedItem?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(chosenName))
        {
            var matches = await _databaseService.SearchStudentsByNamePrefixAsync(chosenName, 1);
            var student = matches.FirstOrDefault();
            if (student != null)
            {
                SelectStudent(student);
            }
        }
    }

    private void SelectStudent(Student student)
    {
        _selectedExistingStudent = student;
        TxtStudentName.Text = student.DisplayName;
        TxtDedupStatus.Text = $"✓ تم ربط الأمر بالملف الموحد للطالب: {student.DisplayName} ({student.Nationality}) لمنع تكرار الهوية";
        TxtDedupStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);

        // Auto-match nationality
        if (student.IsInternational)
        {
            ComboNationalityType.SelectedIndex = 1;
            PanelForeignCountry.Visibility = Visibility.Visible;
            SelectCountryInCombo(student.Nationality);
        }
        else
        {
            ComboNationalityType.SelectedIndex = 0;
            PanelForeignCountry.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectCountryInCombo(string nat)
    {
        for (int i = 0; i < ComboForeignCountry.Items.Count; i++)
        {
            if (ComboForeignCountry.Items[i] is ComboBoxItem item && item.Content?.ToString() == nat)
            {
                ComboForeignCountry.SelectedIndex = i;
                return;
            }
        }
    }

    private void ComboNationalityType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelForeignCountry == null) return;
        bool isIntl = ComboNationalityType.SelectedIndex == 1;
        PanelForeignCountry.Visibility = isIntl ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ComboTrainingStream_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboTrainingStream.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            UpdateStreamPanels(tag);
        }
    }

    private void UpdateStreamPanels(string streamTag)
    {
        if (PanelPart61 == null || PanelEvaluation == null || PanelTypeRating == null || PanelPart141 == null) return;

        PanelPart61.Visibility = streamTag == "Part61" ? Visibility.Visible : Visibility.Collapsed;
        PanelEvaluation.Visibility = streamTag == "Evaluation" ? Visibility.Visible : Visibility.Collapsed;
        PanelTypeRating.Visibility = streamTag == "TypeRating" ? Visibility.Visible : Visibility.Collapsed;
        PanelPart141.Visibility = streamTag == "Part141" ? Visibility.Visible : Visibility.Collapsed;
        if (PanelETP != null) PanelETP.Visibility = streamTag == "ETP" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Cancel close to validate inputs first
        var deferral = args.GetDeferral();
        try
        {
            string studentName = TxtStudentName.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentName))
            {
                ShowError("يرجى إدخال اسم الطالب بالكامل.");
                args.Cancel = true;
                return;
            }

            string orderNumber = TxtOrderNumber.Text.Trim();
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                ShowError("يرجى إدخال رقم أمر التدريب (مثال: 45/2026).");
                args.Cancel = true;
                return;
            }

            // Nationality
            string nationality = "مصري";
            if (ComboNationalityType.SelectedIndex == 1)
            {
                nationality = (ComboForeignCountry.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "وافد";
            }

            // Selected stream
            string streamTag = (ComboTrainingStream.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Part61";

            // Program Type & specific notes
            string programType = "نظام حر";
            string streamNotes = "";

            string batchId = "";
            double syllabusHours = 0;
            string attachments = "";

            if (streamTag == "Part61")
            {
                programType = (ComboPart61Course.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PPL";
            }
            else if (streamTag == "Evaluation")
            {
                programType = (ComboEvalType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "معادلة تقييم";
            }
            else if (streamTag == "TypeRating")
            {
                string act = (ComboTypeActivity.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "تجديد طراز";
                string plane = (ComboFleetAircraft.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "C172";
                double hours = NumFlightHours.Value;
                programType = $"{act} ({plane})";
                streamNotes = $"ساعات مطلوبة: {hours} س | طراز: {plane}";
            }
            else if (streamTag == "Part141")
            {
                string batchProg = TxtBatchProgramName.Text.Trim();
                string batchNum = TxtBatchNumber.Text.Trim();
                programType = string.IsNullOrWhiteSpace(batchNum) ? batchProg : $"{batchProg} - دفعة {batchNum}";
                batchId = batchNum;
                syllabusHours = NumBatchSyllabusHours.Value;
                attachments = TxtOrderAttachments.Text.Trim();
            }
            else if (streamTag == "ETP")
            {
                string route = TxtETPRoute.Text.Trim();
                string air = TxtETPAirline.Text.Trim();
                double hours = NumETPFlightHours.Value;
                programType = $"خط جوي ({route})";
                streamNotes = $"مشغل: {air} | ساعات خط: {hours} س";
                // Keep each operational route independent instead of placing every ETP trainee in one synthetic batch.
                batchId = string.IsNullOrWhiteSpace(route) ? "ETP" : route;
                syllabusHours = hours;
            }

            DateTime enrollDate = PickerEnrollmentDate.Date.HasValue 
                ? PickerEnrollmentDate.Date.Value.DateTime 
                : DateTime.Now;

            DateTime? completeDate = PickerCompletionDate.Date.HasValue 
                ? PickerCompletionDate.Date.Value.DateTime 
                : null;

            int year = int.TryParse((ComboOrderYear.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int yr) 
                ? yr 
                : enrollDate.Year;

            string userNotes = TxtNotes.Text.Trim();
            string combinedNotes = string.IsNullOrWhiteSpace(streamNotes) 
                ? userNotes 
                : (string.IsNullOrWhiteSpace(userNotes) ? streamNotes : $"{streamNotes} | {userNotes}");

            // 1. Create order in SQLite
            CreatedOrder = await _databaseService.CreateManualOrderAsync(
                studentName,
                nationality,
                streamTag,
                programType,
                orderNumber,
                enrollDate,
                completeDate,
                combinedNotes,
                year,
                batchId,
                syllabusHours,
                attachments);

            // 2. Auto-Mirror to Excel in background
            _excelMirrorService?.QueueMirrorSync();

            // 3. Automated Backup Snapshot
            _ = _backupService?.CreateSnapshotAsync("AfterOrderEntry");
        }
        catch (Exception ex)
        {
            ShowError($"حدث خطأ أثناء الحفظ: {ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string msg)
    {
        if (TxtError != null)
        {
            TxtError.Text = msg;
            TxtError.Visibility = Visibility.Visible;
        }
    }
}
