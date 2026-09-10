using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using EAATrainingManager.Helpers;
using EAATrainingManager.Services;
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
            UpdateFileSelectionUI();
            if (LogsItemsControl != null) LogsItemsControl.ItemsSource = ViewModel.SyncLogs;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExcelSyncPage.Loaded] {ex}");
        }
    }

    private void UpdateFileSelectionUI()
    {
        bool hasFile = ViewModel.HasSelectedFile && !string.IsNullOrWhiteSpace(ViewModel.ExcelFilePath) && File.Exists(ViewModel.ExcelFilePath);
        var loc = LocalizationService.Instance;

        if (PanelFileSelected != null)
            PanelFileSelected.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;

        if (PanelNoFile != null)
            PanelNoFile.Visibility = hasFile ? Visibility.Collapsed : Visibility.Visible;

        if (hasFile)
        {
            if (TxtSelectedFileName != null) TxtSelectedFileName.Text = ViewModel.SelectedFileName;
            if (TxtSelectedFileSize != null) TxtSelectedFileSize.Text = ViewModel.SelectedFileSizeText;
            if (TxtSelectedFileDate != null) TxtSelectedFileDate.Text = ViewModel.SelectedFileDateText;
            if (TxtSelectedFullPath != null) TxtSelectedFullPath.Text = ViewModel.ExcelFilePath;
            if (TxtFileReadyBadge != null) TxtFileReadyBadge.Text = loc.Text("✔ جاهز للاستيراد", "✔ Ready to Ingest");
            if (BtnStartImport != null) BtnStartImport.IsEnabled = !ViewModel.IsSyncing;
            if (TxtImportStatus != null)
            {
                TxtImportStatus.Text = ViewModel.SyncProgressText;
                TxtImportStatus.Foreground = new SolidColorBrush(Colors.ForestGreen);
            }
        }
        else
        {
            if (BtnStartImport != null) BtnStartImport.IsEnabled = false;
            if (TxtImportStatus != null)
            {
                TxtImportStatus.Text = loc.Text("يرجى اختيار ملف الإكسيل للمتابعة", "Please select an Excel file to proceed");
                TxtImportStatus.Foreground = new SolidColorBrush(Colors.DarkOrange);
            }
        }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (TxtPageTitle != null)
            TxtPageTitle.Text = loc.Text("مزامنة واستيراد ملفات الإكسيل (ClosedXML)", "Smart Excel Synchronization & Ingestion (ClosedXML)");
        if (TxtPageDesc != null)
            TxtPageDesc.Text = loc.Text("محرك معالجة محلي 100% يعمل بدون إنترنت - تفكيك سجلات أوامر التدريب وإعادة تصدير التقارير الوزارية المعتمدة",
                                         "100% offline local processing engine - parsing legacy training registers and exporting standardized Ministerial reports.");

        if (TxtImportCardTitle != null)
            TxtImportCardTitle.Text = loc.Text("استيراد وتفكيك سجلات التدريب", "Ingest & Deconstruct Training Records");
        if (TxtImportCardSubtitle != null)
            TxtImportCardSubtitle.Text = loc.Text("اختر ملف الإكسيل أو اسحبه هنا بنقرة واحدة دون كتابة أي مسارات",
                                                 "Select or drag & drop your Excel file here in 1 click without typing paths");

        if (TxtBtnChangeFile != null)
            TxtBtnChangeFile.Text = loc.Text("تغيير الملف", "Change File");
        if (TxtDropPrompt != null)
            TxtDropPrompt.Text = loc.Text("اسحب ملف الإكسيل هنا، أو اضغط للاختيار من جهازك", "Drag Excel file here, or click to browse");
        if (TxtBtnBrowseEmpty != null)
            TxtBtnBrowseEmpty.Text = loc.Text("اختيار ملف الإكسيل (تصفح)...", "Browse Excel File...");

        if (TxtImportNotesHeader != null)
            TxtImportNotesHeader.Text = loc.Text("المعالجة التلقائية الذكية:", "Automated Intelligent Ingestion:");
        if (TxtImportNotes != null)
            TxtImportNotes.Text = loc.Text("* يتم تلقائياً قراءة كافة أوراق العمل (تقييم د، نظام حر 61، نظام 141، خط جوي، تجديد طراز) وتوحيد أسماء المتدربين وفصل الرؤوس عن الأوامر.",
                                           "* Automatically parses worksheets (Evaluation, Part 61, Part 141, ATP, Type Rating), deduplicates trainees, and purges headers.");
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

    private async void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var chosenPath = await FilePickerHelper.PickExcelFileAsync(App.MainWindowInstance ?? MainWindow.Current);
            if (!string.IsNullOrWhiteSpace(chosenPath) && File.Exists(chosenPath))
            {
                ViewModel.SetFilePath(chosenPath);
                UpdateFileSelectionUI();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BtnBrowseFile_Click] {ex}");
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = LocalizationService.Instance.IsEnglish ? "Drop Excel Workbook Here" : "أفلت ملف الإكسيل هنا";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;

            if (DropZoneBorder != null)
            {
                DropZoneBorder.BorderBrush = new SolidColorBrush(Colors.DodgerBlue);
                DropZoneBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(30, 13, 110, 253));
            }
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        ResetDropZoneVisual();
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        ResetDropZoneVisual();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        string ext = Path.GetExtension(file.Path).ToLowerInvariant();
                        if (ext == ".xlsx" || ext == ".xls")
                        {
                            ViewModel.SetFilePath(file.Path);
                            UpdateFileSelectionUI();
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DropZone_Drop] {ex}");
        }
    }

    private void ResetDropZoneVisual()
    {
        if (DropZoneBorder != null)
        {
            DropZoneBorder.ClearValue(Border.BorderBrushProperty);
            DropZoneBorder.ClearValue(Border.BackgroundProperty);
        }
    }

    private async void BtnStartImport_Click(object sender, RoutedEventArgs e)
    {
        if (BtnStartImport == null) return;
        if (!ViewModel.HasSelectedFile || string.IsNullOrWhiteSpace(ViewModel.ExcelFilePath))
        {
            BtnBrowseFile_Click(sender, e);
            return;
        }

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
