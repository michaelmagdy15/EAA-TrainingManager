using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.ViewModels;

namespace EAATrainingManager.Views;

public sealed partial class ExcelSyncPage : Page
{
    public ExcelSyncViewModel ViewModel { get; }

    public ExcelSyncPage()
    {
        ViewModel = new ExcelSyncViewModel(App.ExcelSyncService, App.DatabaseService);
        InitializeComponent();
        Loaded += ExcelSyncPage_Loaded;
    }

    private void ExcelSyncPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyLocalization();
            if (TxtFilePath != null) TxtFilePath.Text = ViewModel.ExcelFilePath;
            if (LogsItemsControl != null) LogsItemsControl.ItemsSource = ViewModel.SyncLogs;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExcelSyncPage.Loaded] {ex}");
        }
    }

    private void ApplyLocalization()
    {
        var loc = EAATrainingManager.Services.LocalizationService.Instance;
        if (TxtPageTitle != null)
            TxtPageTitle.Text = loc.Text("مزامنة واستيراد ملفات الإكسيل (ClosedXML)", "Smart Excel Synchronization & Ingestion (ClosedXML)");
        if (TxtPageDesc != null)
            TxtPageDesc.Text = loc.Text("محرك معالجة محلي 100% يعمل بدون إنترنت - تفكيك سجلات أوامر التدريب وإعادة تصدير التقارير الوزارية المعتمدة",
                                         "100% offline local processing engine - parsing legacy training registers and exporting standardized Ministerial reports.");

        if (TxtImportCardTitle != null)
            TxtImportCardTitle.Text = loc.Text("استيراد وتفكيك سجلات التدريب", "Ingest & Deconstruct Training Records");
        if (TxtSourcePathLabel != null)
            TxtSourcePathLabel.Text = loc.Text("مسار ملف الإكسيل المصدر:", "Source Excel File Path:");
        if (TxtImportNotes != null)
            TxtImportNotes.Text = loc.Text("* يتم تلقائياً قراءة أوراق العمل (تقييم د، نظام حر 61، نظام 141، خط جوي، تجديد طراز) وتوحيد أسماء المتدربين وفصل الرؤوس عن الأوامر.",
                                           "* Automatically parses worksheets (Evaluation, Part 61, Part 141, ATP, Type Rating), deduplicates trainees, and purges headers.");
        if (TxtImportStatus != null)
            TxtImportStatus.Text = loc.Text("جاهز للمزامنة", "Ready to Sync");
        if (TxtStartImportBtn != null)
            TxtStartImportBtn.Text = loc.Text("بدء استيراد ومعالجة البيانات", "Start Import & Processing");

        if (TxtExportCardTitle != null)
            TxtExportCardTitle.Text = loc.Text("تصدير التقرير الوزاري الرسمي (RTL)", "Export Official Ministry Report (RTL)");
        if (TxtExportDescLabel != null)
            TxtExportDescLabel.Text = loc.Text("توليد مصنف إكسيل مطابق للمعايير الرسمية لوزارة الطيران المدني:", "Generate official Excel workbook compliant with Civil Aviation Ministry standards:");
        if (TxtExportBullet1 != null)
            TxtExportBullet1.Text = loc.Text("✔ تفعيل اتجاه الورقة من اليمين لليسار (RightToLeft = true)", "✔ Right-to-Left sheet orientation preserved (RightToLeft = true)");
        if (TxtExportBullet2 != null)
            TxtExportBullet2.Text = loc.Text("✔ ترويسة وزارية رسمية بـ 4 أسطر مع تنسيق الألوان الملكية", "✔ 4-line official ministerial header with royal color palette");
        if (TxtExportBullet3 != null)
            TxtExportBullet3.Text = loc.Text("✔ تنسيق أرقام عربية قياسية (Western Arabic 1, 2, 3) لمنع الارتباك", "✔ Standard Arabic numerals (1, 2, 3) to prevent confusion");
        if (TxtExportBullet4 != null)
            TxtExportBullet4.Text = loc.Text("✔ تمييز حالات التدريب النشطة والمنتهية بألوان معيارية", "✔ Standardized color-coding for active vs completed courses");
        if (TxtExportStatus != null)
            TxtExportStatus.Text = loc.Text("سيتم حفظ الملف مباشرة على سطح المكتب", "File will be saved directly to your Desktop");
        if (TxtExportReportBtn != null)
            TxtExportReportBtn.Text = loc.Text("تصدير السجل الوزاري لسطح المكتب", "Export Ministerial Register to Desktop");

        if (TxtAuditLogTitle != null)
            TxtAuditLogTitle.Text = loc.Text("سجل عمليات المزامنة والتدقيق (Sync Audit Log)", "Synchronization & Audit Log Console");
        if (BtnClearLogs != null)
            BtnClearLogs.Content = loc.Text("مسح السجل", "Clear Log");
    }

    private async void BtnStartImport_Click(object sender, RoutedEventArgs e)
    {
        if (TxtFilePath == null || BtnStartImport == null) return;
        ViewModel.ExcelFilePath = TxtFilePath.Text.Trim();
        if (ImportProgressBar != null)
        {
            ImportProgressBar.Visibility = Visibility.Visible;
            ImportProgressBar.IsIndeterminate = true;
        }
        BtnStartImport.IsEnabled = false;

        try
        {
            await ViewModel.StartImportAsync();
            if (TxtImportStatus != null) TxtImportStatus.Text = ViewModel.SyncProgressText;
        }
        catch (Exception ex)
        {
            if (TxtImportStatus != null) TxtImportStatus.Text = $"خطأ: {ex.Message}";
        }
        finally
        {
            if (ImportProgressBar != null)
            {
                ImportProgressBar.Visibility = Visibility.Collapsed;
                ImportProgressBar.IsIndeterminate = false;
            }
            BtnStartImport.IsEnabled = true;
            if (LogsItemsControl != null)
            {
                LogsItemsControl.ItemsSource = null;
                LogsItemsControl.ItemsSource = ViewModel.SyncLogs;
            }
        }
    }

    private async void BtnExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (BtnExportReport == null) return;
        BtnExportReport.IsEnabled = false;
        try
        {
            await ViewModel.ExportMinisterialReportAsync();
            if (TxtExportStatus != null) TxtExportStatus.Text = ViewModel.ExportStatusMessage;
        }
        catch (Exception ex)
        {
            if (TxtExportStatus != null) TxtExportStatus.Text = $"خطأ أثناء التصدير: {ex.Message}";
        }
        finally
        {
            BtnExportReport.IsEnabled = true;
        }
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.SyncLogs.Clear();
        }
        catch { }
    }
}
