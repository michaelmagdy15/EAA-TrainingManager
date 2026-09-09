using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using EAATrainingManager.Helpers;
using EAATrainingManager.Models;

namespace EAATrainingManager.Services;

public record SyncResult(
    int TotalRowsScanned,
    int OrdersImported,
    int UniqueStudentsCount,
    int ActiveOrdersCount,
    int CompletedOrdersCount,
    List<string> ProcessedSheets,
    List<string> LogMessages
);

public class ExcelSyncService
{
    private readonly DatabaseService _dbService;

    public ExcelSyncService(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    /// <summary>
    /// Ingests and normalizes training records from an EAA Excel workbook (e.g. 2اوامر التدريب.xlsx).
    /// Decouples student identity from order entries, eliminates duplicate trainee headcounts,
    /// standardizes curriculum shifts (PPL -> CPL/IR -> ATP), and detects active vs completed pipeline statuses.
    /// </summary>
    public async Task<SyncResult> ImportFromWorkbookAsync(string filePath, Action<int, int, string>? onProgress = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Excel file not found", filePath);

        var logMessages = new List<string>();
        var processedSheets = new List<string>();
        int totalScanned = 0;
        int ordersImported = 0;
        int activeCount = 0;
        int completedCount = 0;

        await _dbService.InitializeAsync();

        using var workbook = new XLWorkbook(filePath);

        // Process priority sheets
        var targetSheets = workbook.Worksheets.ToList();
        int sheetIndex = 0;

        foreach (var worksheet in targetSheets)
        {
            sheetIndex++;
            string sheetName = worksheet.Name.Trim();
            logMessages.Add($"بدء فحص الورقة: {sheetName}");

            // Determine sheet category & default curriculum via robust multi-stream signature matching
            string category = sheetName;
            string defaultProgram = "تقييم";

            if (sheetName.Contains("61") || sheetName.Contains("حر"))
            {
                category = "61 (ج نظام حر)";
                defaultProgram = "نظام حر 61";
            }
            else if (sheetName.Contains("141") || sheetName.Contains("نظام") || sheetName.Contains("دفعات"))
            {
                category = "141 ( ا نظام )";
                defaultProgram = "نظام معتمد 141";
            }
            else if (sheetName.Contains("خط جوي") || sheetName.Contains("ATP") || sheetName.Contains("atp"))
            {
                category = "خط جوي (هــ)";
                defaultProgram = "طيار خط جوي (ATP)";
            }
            else if (sheetName.Contains("طراز") || sheetName.Contains("فرق") || sheetName.Contains("ساعات"))
            {
                category = "تجديد طراز ( و )";
                defaultProgram = "تجديد طراز وفرق";
            }
            else if (sheetName.Contains("تقييم") || sheetName.Contains("معادلة"))
            {
                category = "تقييم (د)";
                defaultProgram = "تقييم ومعادلة";
            }

            int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow < 7) continue;

            int currentYear = 2026;
            int sheetOrdersCount = 0;

            for (int r = 1; r <= lastRow; r++)
            {
                totalScanned++;
                var row = worksheet.Row(r);

                // Check for year headers (e.g. "لسنة 2025" or "لسنة 2026")
                string rowText = string.Join(" ", row.Cells(1, 10).Select(c => c.GetString().Trim()));
                if (rowText.Contains("2025"))
                    currentYear = 2025;
                else if (rowText.Contains("2026"))
                    currentYear = 2026;

                // Skip pure header rows
                if (rowText.Contains("وزارة الطيران") || rowText.Contains("الاكاديمية") ||
                    rowText.Contains("الكلية") || rowText.Contains("إدارة التدريب") ||
                    rowText.Contains("بيان بأسماء") || rowText.Contains("Column1"))
                {
                    continue;
                }

                // Check if this is a column header row
                if (row.Cells(1, 5).Any(c => c.GetString().Trim() == "م" || c.GetString().Trim() == "الإسم" || c.GetString().Trim() == "اسم البرنامج التدريبى"))
                {
                    continue;
                }

                // Extract values based on sheet layout
                string studentName = string.Empty;
                string nationality = "مصري";
                string orderNumber = string.Empty;
                DateTime? enrollDate = null;
                DateTime? compDate = null;
                string notes = string.Empty;
                int seqNum = 0;

                if (sheetName.Contains("تقييم (د)"))
                {
                    // Layout: Col 1: م, Col 2: الإسم, Col 3: الجنسية, Col 4: رقم امر التدريب, Col 5: تاريخ الالتحاق, Col 6: تاريخ النهاية, Col 7: ملاحظات
                    seqNum = TryParseInt(row.Cell(1));
                    studentName = row.Cell(2).GetString().Trim();
                    nationality = row.Cell(3).GetString().Trim();
                    orderNumber = row.Cell(4).GetString().Trim();
                    enrollDate = TryParseDate(row.Cell(5));
                    compDate = TryParseDate(row.Cell(6));
                    notes = row.Cell(7).GetString().Trim();
                }
                else if (sheetName.Contains("61"))
                {
                    // Layout: Col 2: م, Col 3: الإسم, Col 4: الجنسية, Col 5: رقم امر التدريب, Col 6: تاريخ الالتحاق, Col 7: ملاحظات
                    seqNum = TryParseInt(row.Cell(2));
                    studentName = row.Cell(3).GetString().Trim();
                    nationality = row.Cell(4).GetString().Trim();
                    orderNumber = row.Cell(5).GetString().Trim();
                    enrollDate = TryParseDate(row.Cell(6));
                    notes = row.Cell(7).GetString().Trim();
                }
                else if (sheetName.Contains("خط جوي"))
                {
                    // Layout: Col 2: م, Col 3: الإسم, Col 4: الجنسية, Col 5: رقم امر التدريب, Col 6: ملاحظات
                    seqNum = TryParseInt(row.Cell(2));
                    studentName = row.Cell(3).GetString().Trim();
                    nationality = row.Cell(4).GetString().Trim();
                    orderNumber = row.Cell(5).GetString().Trim();
                    notes = row.Cell(6).GetString().Trim();
                }
                else if (sheetName.Contains("تجديد طراز"))
                {
                    // Layout: Col 2: م, Col 3: الإسم, Col 4: الجنسية, Col 5: التاريخ, Col 6: رقم امر التدريب, Col 7: ملاحظات
                    seqNum = TryParseInt(row.Cell(2));
                    studentName = row.Cell(3).GetString().Trim();
                    nationality = row.Cell(4).GetString().Trim();
                    enrollDate = TryParseDate(row.Cell(5));
                    orderNumber = row.Cell(6).GetString().Trim();
                    notes = row.Cell(7).GetString().Trim();
                }

                // If no valid student name found, skip row
                if (string.IsNullOrWhiteSpace(studentName) || studentName.Length < 3 || studentName == "الإسم")
                    continue;

                if (string.IsNullOrWhiteSpace(nationality))
                    nationality = "مصري";

                if (string.IsNullOrWhiteSpace(orderNumber))
                    orderNumber = seqNum > 0 ? seqNum.ToString() : (sheetOrdersCount + 1).ToString();

                // Derive ProgramType from notes if specialized
                string program = defaultProgram;
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    if (notes.Contains("ATP")) program = "معادلة ATP";
                    else if (notes.Contains("PPL")) program = "معادلة PPL";
                    else if (notes.Contains("CPL") || notes.Contains("IR")) program = "استكمال IR/CPL";
                    else if (notes.Contains("CESSNA")) program = "طراز Cessna";
                }

                // 1. Get or Create Unified Student (DECOUPLING trainee identity from order row!)
                int studentId = await _dbService.GetOrCreateStudentAsync(studentName, nationality);

                // 2. Build order with automated pipeline status and multi-stream classification
                int acadYear = enrollDate.HasValue && enrollDate.Value.Year >= 2020 ? enrollDate.Value.Year : currentYear;
                string regTrack = DatabaseService.ClassifyRegulatoryTrack(category, program);

                var order = new TrainingOrder
                {
                    StudentId = studentId,
                    OrderNumber = orderNumber,
                    ProgramType = program,
                    RegulationCategory = category,
                    EnrollmentDate = enrollDate,
                    CompletionDate = compDate,
                    Notes = notes,
                    Year = currentYear,
                    AcademicYear = acadYear,
                    RegulatoryTrack = regTrack,
                    SequenceNumber = seqNum > 0 ? seqNum : sheetOrdersCount + 1
                };

                await _dbService.InsertOrUpdateOrderAsync(order);
                sheetOrdersCount++;
                ordersImported++;

                if (order.IsActive) activeCount++;
                else completedCount++;

                if (ordersImported % 10 == 0)
                {
                    onProgress?.Invoke(sheetIndex, targetSheets.Count, $"تم استيراد {ordersImported} أمر تدريب...");
                }
            }

            if (sheetOrdersCount > 0)
            {
                processedSheets.Add($"{sheetName} ({sheetOrdersCount} أمر)");
                logMessages.Add($"تم استيراد {sheetOrdersCount} أمر تدريب من ورقة '{sheetName}'");
            }
        }

        var metrics = await _dbService.GetDashboardMetricsAsync();

        logMessages.Add($"اكتملت المزامنة بنجاح: تم استيراد {ordersImported} أمر تدريب، وتحديث {metrics.TotalUniqueStudents} طالب فعلي.");

        return new SyncResult(
            totalScanned,
            ordersImported,
            metrics.TotalUniqueStudents,
            activeCount,
            completedCount,
            processedSheets,
            logMessages
        );
    }

    /// <summary>
    /// Exports an official, compliant Ministry of Civil Aviation spreadsheet logbook
    /// with strict RTL geometry (worksheet.RightToLeft = true), EAA branding, and Western Arabic numerals.
    /// </summary>
    public async Task<string> ExportOfficialMinistryReportAsync(string targetFilePath, string? filterStatus = null, int? filterYear = null)
    {
        var orders = await _dbService.GetAllOrdersAsync(filterYear, null, filterStatus);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("أوامر التدريب");

        // Set strict native RTL compatibility
        ws.RightToLeft = true;

        // 1. EAA Official 4-Line Ministerial Header
        ws.Cell("A1").Value = "جمهورية مصر العربية";
        ws.Cell("A2").Value = "وزارة الطيران المدني – الاكاديمية المصرية لعلوم الطيران";
        ws.Cell("A3").Value = "الكلية المصرية للطيران – إدارة التدريب";
        ws.Cell("A4").Value = $"سجل بيانات أوامر التدريب والمتدربين - تاريخ الاستخراج: {DateTime.Now:yyyy/MM/dd}";

        for (int i = 1; i <= 4; i++)
        {
            var r = ws.Row(i);
            r.Height = 22;
            ws.Range(i, 1, i, 8).Merge();
            ws.Cell(i, 1).Style.Font.Bold = true;
            ws.Cell(i, 1).Style.Font.FontName = "Segoe UI";
            ws.Cell(i, 1).Style.Font.FontSize = i == 1 ? 13 : 11;
            ws.Cell(i, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(i, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F497D");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;

        ws.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#2C5E9E");
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.White;

        ws.Cell(3, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#1F497D");

        ws.Cell(4, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F5F9");
        ws.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#333333");

        // 2. Table Column Headers at Row 6
        string[] headers =
        {
            "م",
            "اسم المتدرب / الطالب",
            "الجنسية",
            "رقم أمر التدريب",
            "البرنامج التدريبي",
            "تاريخ الالتحاق",
            "تاريخ النهاية",
            "حالة التدريب",
            "ملاحظات"
        };

        var headerRow = ws.Row(6);
        headerRow.Height = 26;

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(6, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontName = "Segoe UI";
            cell.Style.Font.FontSize = 10.5;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F497D");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#0B2240");
        }

        // 3. Data Rows
        int rowIdx = 7;
        int seq = 1;

        foreach (var order in orders)
        {
            var r = ws.Row(rowIdx);
            r.Height = 22;

            ws.Cell(rowIdx, 1).SetValue(seq++);
            ws.Cell(rowIdx, 2).SetValue(order.StudentDisplayName);
            ws.Cell(rowIdx, 3).SetValue(order.Nationality);
            ws.Cell(rowIdx, 4).SetValue(order.OrderNumber);
            ws.Cell(rowIdx, 5).SetValue(order.ProgramType);
            ws.Cell(rowIdx, 6).SetValue(order.EnrollmentDate.HasValue ? order.EnrollmentDate.Value.ToString("yyyy/MM/dd") : "-");
            ws.Cell(rowIdx, 7).SetValue(order.CompletionDate.HasValue ? order.CompletionDate.Value.ToString("yyyy/MM/dd") : "مستمر (قيد التدريب)");
            ws.Cell(rowIdx, 8).SetValue(order.Status);
            ws.Cell(rowIdx, 9).SetValue(order.Notes);

            // Alignment & Borders
            for (int col = 1; col <= headers.Length; col++)
            {
                var cell = ws.Cell(rowIdx, col);
                cell.Style.Font.FontName = "Segoe UI";
                cell.Style.Font.FontSize = 10;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D9D9D9");
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                if (col == 1 || col == 3 || col == 4 || col == 6 || col == 7 || col == 8)
                {
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
            }

            // Zebra striping
            if (rowIdx % 2 == 0)
            {
                ws.Range(rowIdx, 1, rowIdx, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }

            // Status color highlight
            var statusCell = ws.Cell(rowIdx, 8);
            if (order.IsActive)
            {
                statusCell.Style.Font.FontColor = XLColor.FromHtml("#0D6EFD"); // Active Blue
                statusCell.Style.Font.Bold = true;
            }
            else
            {
                statusCell.Style.Font.FontColor = XLColor.FromHtml("#198754"); // Completed Green
            }

            rowIdx++;
        }

        // Auto-fit column widths with generous margin
        ws.Columns(1, headers.Length).AdjustToContents();
        for (int c = 1; c <= headers.Length; c++)
        {
            ws.Column(c).Width = Math.Max(ws.Column(c).Width + 4, 14);
        }
        ws.Column(2).Width = 32; // Student Name
        ws.Column(9).Width = 28; // Notes

        workbook.SaveAs(targetFilePath);
        return targetFilePath;
    }

    /// <summary>
    /// Exports the dedicated official roster of international / foreign trainees
    /// formatted specifically for the Central Administration of Civil Aviation and consular security records.
    /// Uses native RTL, Western Arabic numerals, and demographic status metrics.
    /// </summary>
    public async Task<string> ExportInternationalStudentsRosterAsync(string targetFilePath, int? filterYear = null)
    {
        var intlOrders = await _dbService.GetAllOrdersAsync(filterYear, null, null, null, null, true);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("الطلبة الوافدون");

        // Strict native RTL format
        ws.RightToLeft = true;

        // 1. Official 4-Line Ministerial & Consular Header
        string yearLabel = filterYear.HasValue ? $"لسنة {filterYear.Value}" : "لكافة السنوات الدراسية";
        ws.Cell("A1").Value = "جمهورية مصر العربية";
        ws.Cell("A2").Value = "وزارة الطيران المدني – الأكاديمية المصرية لعلوم الطيران";
        ws.Cell("A3").Value = "الكلية المصرية للطيران – إدارة التدريب وشؤون الوافدين";
        ws.Cell("A4").Value = $"كشف حصر ومتابعة الطلبة الوافدين المعتمد للإدارة المركزية للطيران المدني ({yearLabel}) - استخراج: {DateTime.Now:yyyy/MM/dd}";

        for (int i = 1; i <= 4; i++)
        {
            var r = ws.Row(i);
            r.Height = 22;
            ws.Range(i, 1, i, 11).Merge();
            ws.Cell(i, 1).Style.Font.Bold = true;
            ws.Cell(i, 1).Style.Font.FontName = "Segoe UI";
            ws.Cell(i, 1).Style.Font.FontSize = i == 1 ? 13 : 11;
            ws.Cell(i, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(i, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0F2027");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;

        ws.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#203A43");
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.White;

        ws.Cell(3, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#2C5364");
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.White;

        ws.Cell(4, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F5F9");
        ws.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#111827");

        // 2. Table Column Headers
        string[] headers =
        {
            "م",
            "اسم المتدرب / الطالب",
            "الجنسية",
            "المسار التدريبي",
            "رقم أمر التدريب",
            "البرنامج التدريبي",
            "السنة",
            "تاريخ الالتحاق",
            "تاريخ النهاية",
            "حالة التدريب",
            "ملاحظات"
        };

        var headerRow = ws.Row(6);
        headerRow.Height = 26;

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(6, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontName = "Segoe UI";
            cell.Style.Font.FontSize = 10.5;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F497D");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#0B2240");
        }

        // 3. Data Rows
        int rowIdx = 7;
        int seq = 1;
        var distinctNationalities = new HashSet<string>();

        foreach (var order in intlOrders)
        {
            var r = ws.Row(rowIdx);
            r.Height = 22;

            distinctNationalities.Add(DemographicsEngine.StandardizeNationality(order.Nationality));

            ws.Cell(rowIdx, 1).SetValue(seq++);
            ws.Cell(rowIdx, 2).SetValue(order.StudentDisplayName);
            ws.Cell(rowIdx, 3).SetValue(DemographicsEngine.StandardizeNationality(order.Nationality));
            ws.Cell(rowIdx, 4).SetValue(order.RegulatoryTrackBadgeText);
            ws.Cell(rowIdx, 5).SetValue(order.OrderNumber);
            ws.Cell(rowIdx, 6).SetValue(order.ProgramType);
            ws.Cell(rowIdx, 7).SetValue(order.AcademicYear);
            ws.Cell(rowIdx, 8).SetValue(order.EnrollmentDate.HasValue ? order.EnrollmentDate.Value.ToString("yyyy/MM/dd") : "-");
            ws.Cell(rowIdx, 9).SetValue(order.CompletionDate.HasValue ? order.CompletionDate.Value.ToString("yyyy/MM/dd") : "مستمر (قيد التدريب)");
            ws.Cell(rowIdx, 10).SetValue(order.Status);
            ws.Cell(rowIdx, 11).SetValue(order.Notes);

            for (int col = 1; col <= headers.Length; col++)
            {
                var cell = ws.Cell(rowIdx, col);
                cell.Style.Font.FontName = "Segoe UI";
                cell.Style.Font.FontSize = 10;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D9D9D9");
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                if (col == 1 || col == 3 || col == 4 || col == 5 || col == 7 || col == 8 || col == 9 || col == 10)
                {
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
            }

            if (rowIdx % 2 == 0)
            {
                ws.Range(rowIdx, 1, rowIdx, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }

            var statusCell = ws.Cell(rowIdx, 10);
            if (order.IsActive)
            {
                statusCell.Style.Font.FontColor = XLColor.FromHtml("#0D6EFD");
                statusCell.Style.Font.Bold = true;
            }
            else
            {
                statusCell.Style.Font.FontColor = XLColor.FromHtml("#198754");
            }

            rowIdx++;
        }

        // Summary Statistics Row
        int summaryRow = rowIdx + 1;
        ws.Range(summaryRow, 1, summaryRow, 4).Merge();
        ws.Cell(summaryRow, 1).Value = $"إجمالي أوامر الوافدين: {intlOrders.Count} أمر | إجمالي الجنسيات: {distinctNationalities.Count} دولة";
        ws.Cell(summaryRow, 1).Style.Font.Bold = true;
        ws.Cell(summaryRow, 1).Style.Font.FontName = "Segoe UI";
        ws.Cell(summaryRow, 1).Style.Font.FontSize = 11;
        ws.Cell(summaryRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
        ws.Cell(summaryRow, 1).Style.Font.FontColor = XLColor.FromHtml("#856404");
        ws.Cell(summaryRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(summaryRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(summaryRow).Height = 24;

        ws.Columns(1, headers.Length).AdjustToContents();
        for (int c = 1; c <= headers.Length; c++)
        {
            ws.Column(c).Width = Math.Max(ws.Column(c).Width + 4, 14);
        }
        ws.Column(2).Width = 32;
        ws.Column(11).Width = 28;

        workbook.SaveAs(targetFilePath);
        return targetFilePath;
    }

    private static int TryParseInt(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        if (cell.TryGetValue<int>(out int val)) return val;
        string s = cell.GetString().Trim();
        if (int.TryParse(s, out int parsed)) return parsed;
        return 0;
    }

    private static DateTime? TryParseDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<DateTime>(out DateTime dt))
        {
            // Safeguard against default zero dates
            if (dt.Year > 1990 && dt.Year < 2050)
                return dt;
        }

        string s = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        // Try standard culture formats
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ||
            DateTime.TryParse(s, new CultureInfo("ar-EG"), DateTimeStyles.None, out dt) ||
            DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "yyyy/MM/dd", "d/M/yyyy", "yyyy-MM-dd HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
        {
            if (dt.Year > 1990 && dt.Year < 2050)
                return dt;
        }

        return null;
    }
}
