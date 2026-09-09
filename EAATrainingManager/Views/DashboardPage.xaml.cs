using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EAATrainingManager.Services;
using EAATrainingManager.ViewModels;

namespace EAATrainingManager.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    private bool _isLoaded;

    public DashboardPage()
    {
        ViewModel = new DashboardViewModel(App.DatabaseService, App.ExcelSyncService);
        InitializeComponent();
        Loaded += DashboardPage_Loaded;
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ApplyLocalization();
        await LoadDataAsync();
    }

    private async void YearSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        if (YearSelector.SelectedItem is ComboBoxItem item)
        {
            string? tag = item.Tag?.ToString();
            await ViewModel.SetAcademicYearAsync(tag);
            UpdateUI();
        }
    }

    private async void BtnExportConsular_Click(object sender, RoutedEventArgs e)
    {
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "جاري تصدير كشف الطلبة الوافدين للإدارة المركزية للطيران المدني...";

        await ViewModel.ExportInternationalRosterAsync();

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = ViewModel.StatusMessage;
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async void BtnAddOrder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.AddOrderDialog(App.DatabaseService, App.ExcelMirrorService, App.BackupService)
        {
            XamlRoot = this.XamlRoot
        };
        var res = await dlg.ShowAsync();
        if (res == ContentDialogResult.Primary && dlg.CreatedOrder != null)
        {
            await LoadDataAsync();
        }
    }

    private async void BtnOpenArchive_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.ArchivedOrdersDialog(App.DatabaseService, App.ExcelMirrorService)
        {
            XamlRoot = this.XamlRoot
        };
        await dlg.ShowAsync();
        if (dlg.HasChanged)
        {
            await LoadDataAsync();
        }
    }

    private async void BtnQuickImport_Click(object sender, RoutedEventArgs e)
    {
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "جاري استيراد وتفكيك سجلات 2اوامر التدريب.xlsx...";

        await ViewModel.QuickImportDefaultExcelAsync();
        UpdateUI();

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = ViewModel.StatusMessage;
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        await ViewModel.LoadMetricsAsync();
        UpdateUI();
    }

    private void UpdateUI()
    {
        TxtTotalUniqueStudents.Text = ViewModel.TotalUniqueStudents.ToString();
        TxtInternationalStudents.Text = ViewModel.InternationalStudentsCount.ToString();
        TxtInternationalPercent.Text = ViewModel.InternationalPercentageText;
        TxtPart61Count.Text = ViewModel.Part61Count.ToString();
        TxtPart141Count.Text = ViewModel.Part141Count.ToString();

        DistributionItemsControl.ItemsSource = ViewModel.ProgramDistribution;
        RecentOrdersListView.ItemsSource = ViewModel.RecentOrders;
        NationalitiesListView.ItemsSource = ViewModel.TopNationalities;

        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Message = ViewModel.StatusMessage;
    }

    private void ApplyLocalization()
    {
        bool isEn = LocalizationService.Instance.IsEnglish;
        this.FlowDirection = LocalizationService.Instance.CurrentFlowDirection;

        if (TxtHeaderSubtitle != null) TxtHeaderSubtitle.Text = isEn ? "Egyptian Aviation Academy – Flight Training Directorate" : "الكلية المصرية للطيران – إدارة التدريب";
        if (TxtHeaderTitle != null) TxtHeaderTitle.Text = isEn ? "Flight Training Operations & Analytics Dashboard" : "لوحة المؤشرات والقياس العملياتي لأوامر التدريب";
        if (TxtHeaderDesc != null) TxtHeaderDesc.Text = isEn ? "Trainee Headcount Deduplication & Flight Syllabus Tracking (PPL ➔ CPL/IR ➔ ATP)" : "نظام حصر المتدربين الفعليين وفصل الرؤوس عن أوامر التدريب مع توحيد مسارات (PPL ➔ CPL/IR ➔ ATP)";
        
        if (TxtYearLabel != null) TxtYearLabel.Text = isEn ? "Academic Year:" : "السنة التدريبية:";
        if (TxtExportConsular != null) TxtExportConsular.Text = isEn ? "International Roster" : "كشف الوافدين";
        if (TxtBtnAddOrder != null) TxtBtnAddOrder.Text = isEn ? "New Order" : "أمر تدريب جديد";
        if (TxtBtnOpenArchive != null) TxtBtnOpenArchive.Text = isEn ? "Archive" : "الأرشيف";
        if (TxtQuickImport != null) TxtQuickImport.Text = isEn ? "Quick Sync" : "مزامنة سريعة";

        if (TxtKpiStudentsTitle != null) TxtKpiStudentsTitle.Text = isEn ? "Total Unique Trainees" : "إجمالي الطلبة الفعليين";
        if (TxtKpiStudentsPill != null) TxtKpiStudentsPill.Text = isEn ? "Actual Headcount" : "الرؤوس الفعلية";
        if (TxtKpiStudentsDesc != null) TxtKpiStudentsDesc.Text = isEn ? "Unique human cadets (deduplicated)" : "متدرب كشخص (مستقل عن تكرار الأوامر)";

        if (TxtKpiIntlTitle != null) TxtKpiIntlTitle.Text = isEn ? "International Cadets" : "الطلبة الوافدون (أجانب وعرب)";
        if (TxtKpiIntlPill != null) TxtKpiIntlPill.Text = isEn ? "Demographics" : "شؤون الوافدين";

        if (TxtKpiPart61Title != null) TxtKpiPart61Title.Text = isEn ? "Part 61 (Modular)" : "النظام الحر (Part 61)";
        if (TxtKpiPart61Pill != null) TxtKpiPart61Pill.Text = isEn ? "Part 61" : "نظام حر";
        if (TxtKpiPart61Desc != null) TxtKpiPart61Desc.Text = isEn ? "Self-paced modular flight training" : "أوامر تدريب نظام حر معتمد";

        if (TxtKpiPart141Title != null) TxtKpiPart141Title.Text = isEn ? "Part 141 (Integrated)" : "النظام المعتمد (Part 141)";
        if (TxtKpiPart141Pill != null) TxtKpiPart141Pill.Text = isEn ? "Part 141" : "دفعات نظامية";
        if (TxtKpiPart141Desc != null) TxtKpiPart141Desc.Text = isEn ? "Integrated airline cadet batches" : "طلبة الدفعات والبرامج المعتمدة";

        if (TxtMilestonesTitle != null) TxtMilestonesTitle.Text = isEn ? "Trainee Distribution by Milestone & Regulatory Track" : "توزيع المتدربين حسب مراحل المنهج ومسارات الطيران (بدون احتساب مزدوج)";
        if (TxtMilestonesDesc != null) TxtMilestonesDesc.Text = isEn ? "* Legacy split orders (CPL + IR) unified under CPL/IR" : "* يتم ضم الأوامر القديمة المنفصلة (CPL + IR) مع الأوامر المدمجة (CPL/IR)";

        if (TxtRecentOrdersTitle != null) TxtRecentOrdersTitle.Text = isEn ? "Recent Flight Training Orders" : "سجل أوامر التدريب الحديثة";
        if (TxtRecentOrdersSubtitle != null) TxtRecentOrdersSubtitle.Text = isEn ? "Latest processed records in selected academic year" : "أحدث السجلات المعالجة بالسنة المختارة";

        if (ColHeaderSeq != null) ColHeaderSeq.Text = isEn ? "#" : "م";
        if (ColHeaderStudent != null) ColHeaderStudent.Text = isEn ? "Cadet / Trainee Name" : "اسم المتدرب / الطالب";
        if (ColHeaderNationality != null) ColHeaderNationality.Text = isEn ? "Nationality" : "الجنسية";
        if (ColHeaderOrderNum != null) ColHeaderOrderNum.Text = isEn ? "Order #" : "رقم الأمر";
        if (ColHeaderTrack != null) ColHeaderTrack.Text = isEn ? "Track & Syllabus" : "المسار والمنهج";
        if (ColHeaderDate != null) ColHeaderDate.Text = isEn ? "Enrollment Date" : "تاريخ الالتحاق";
        if (ColHeaderStatus != null) ColHeaderStatus.Text = isEn ? "Status" : "الحالة";

        if (TxtNationalitiesTitle != null) TxtNationalitiesTitle.Text = isEn ? "International Demographics" : "توزيع جنسيات الوافدين";
        if (TxtNationalitiesPill != null) TxtNationalitiesPill.Text = isEn ? "Country Density" : "كثافة الدول";
        if (TxtNationalitiesDesc != null) TxtNationalitiesDesc.Text = isEn ? "Exclusive breakdown of international trainees" : "بيان حصري بجنسيات وأعداد الطلبة الأجانب والعرب";

        if (ComboItemAllYears != null) ComboItemAllYears.Content = isEn ? "All Years" : "جميع السنوات";
    }
}
