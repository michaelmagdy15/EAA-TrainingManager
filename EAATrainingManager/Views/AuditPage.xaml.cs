using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EAATrainingManager.Models;
using EAATrainingManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EAATrainingManager.Views;

public sealed partial class AuditPage : Page
{
    public AuditPage()
    {
        InitializeComponent();
        ConfigureLanguage();
        foreach (string action in new[] { "All", "Created", "Updated", "Completed", "Archived", "Restored", "StatusChanged", "Approved", "Consumed", "Login", "Logout" })
            ActionFilter.Items.Add(new ComboBoxItem { Content = action, Tag = action == "All" ? string.Empty : action });
        ActionFilter.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await LoadAuditAsync();

    private async void ApplyFilters_Click(object sender, RoutedEventArgs e) => await LoadAuditAsync();

    private async Task<bool> LoadAuditAsync()
    {
        try
        {
            UserSession? session = App.IdentityService.CurrentSession;
            if (session == null)
                throw new UnauthorizedAccessException("An authenticated audit session is required.");

            await App.IdentityService.RequirePermissionAsync(session.SessionId, "audit", PermissionLevel.ReadOnly);
            string? action = (ActionFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            DateTime? from = FromDatePicker.Date?.DateTime.Date;
            DateTime? to = ToDatePicker.Date?.DateTime.Date.AddDays(1);
            int? entityId = int.TryParse(EntityIdFilter.Text, out int parsedEntityId) ? parsedEntityId : null;
            var events = await App.DatabaseService.GetAuditEventsAsync(
                entityType: NullIfWhiteSpace(EntityFilter.Text),
                entityId: entityId,
                limit: 10_000,
                from: from,
                to: to,
                actor: NullIfWhiteSpace(ActorFilter.Text),
                action: NullIfWhiteSpace(action));
            AuditList.ItemsSource = events;
            ResultCount.Text = LocalizationService.Instance.Text($"عدد النتائج: {events.Count}", $"Results: {events.Count}");
            FilterStatus.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception ex)
        {
            AppLogService.LogException("AuditPage.Load", ex, "AuditEvents");
            ShowStatus(LocalizationService.Instance.Text("تعذر تحميل سجل التدقيق أو لا توجد صلاحية.", "Audit history could not be loaded or access is denied."));
            return false;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await LoadAuditAsync())
                return;
            if (AuditList.ItemsSource is not System.Collections.Generic.IEnumerable<AuditEvent> events)
                return;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string exportDirectory = Path.Combine(localAppData, "EAA_TrainingManager", "Exports");
            Directory.CreateDirectory(exportDirectory);
            string exportPath = Path.Combine(exportDirectory, $"audit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
            var csv = new StringBuilder("OccurredAt,Actor,UserId,SessionId,LocationId,Action,EntityType,EntityId,VersionNo,Summary,BeforeJson,AfterJson\r\n");
            foreach (AuditEvent auditEvent in events)
            {
                csv.AppendLine(string.Join(",", new[]
                {
                    Csv(auditEvent.OccurredAt.ToString("o", CultureInfo.InvariantCulture)),
                    Csv(auditEvent.Actor),
                    Csv(auditEvent.UserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(auditEvent.SessionId ?? string.Empty),
                    Csv(auditEvent.LocationId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    Csv(auditEvent.Action), Csv(auditEvent.EntityType),
                    Csv(auditEvent.EntityId.ToString(CultureInfo.InvariantCulture)),
                    Csv(auditEvent.VersionNo.ToString(CultureInfo.InvariantCulture)), Csv(auditEvent.Summary),
                    Csv(auditEvent.BeforeJson ?? string.Empty), Csv(auditEvent.AfterJson ?? string.Empty)
                }));
            }

            await File.WriteAllTextAsync(exportPath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ShowStatus(LocalizationService.Instance.Text($"تم تصدير {events.Count()} سجل إلى: {exportPath}", $"Exported {events.Count()} events to: {exportPath}"), isError: false);
        }
        catch (Exception ex)
        {
            AppLogService.LogException("AuditPage.Export", ex, "AuditEvents");
            ShowStatus(LocalizationService.Instance.Text("تعذر تصدير سجل التدقيق.", "Audit history export failed."));
        }
    }

    private void ConfigureLanguage()
    {
        LocalizationService localization = LocalizationService.Instance;
        FlowDirection = localization.CurrentFlowDirection;
        PageTitle.Text = localization.NavAudit;
        PageSubtitle.Text = localization.Text("سجل غير قابل للتعديل للعمليات والاعتمادات", "Append-only history for operational actions and approvals");
        FromDatePicker.PlaceholderText = localization.Text("من تاريخ", "From date");
        ToDatePicker.PlaceholderText = localization.Text("إلى تاريخ", "To date");
        ActorFilter.PlaceholderText = localization.Text("المستخدم", "User");
        EntityFilter.PlaceholderText = localization.Text("نوع السجل", "Entity type");
        EntityIdFilter.PlaceholderText = localization.Text("رقم السجل", "Entity ID");
        ApplyFiltersButton.Content = localization.Text("تطبيق", "Apply");
        ExportButton.Content = localization.Text("تصدير CSV", "Export CSV");
    }

    private void ShowStatus(string message, bool isError = true)
    {
        FilterStatus.Text = message;
        FilterStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(isError ? Microsoft.UI.Colors.OrangeRed : Microsoft.UI.Colors.LightGreen);
        FilterStatus.Visibility = Visibility.Visible;
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
