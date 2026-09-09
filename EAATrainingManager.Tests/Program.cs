using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using EAATrainingManager.Helpers;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Tests;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Configure Egyptian Civil Aviation culture (Western Arabic numerals 1, 2, 3)
        var culture = new CultureInfo("ar-EG");
        culture.NumberFormat.DigitSubstitution = DigitShapes.None;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        Console.WriteLine("===============================================================================");
        Console.WriteLine("  Egyptian Aviation Academy (EAA) Training Management System - Verification Suite");
        Console.WriteLine("===============================================================================\n");

        int failures = 0;

        // TEST SUITE 1: Arabic Normalization & BiDi Formatting
        Console.WriteLine("[TEST SUITE 1] Testing ArabicTextHelper Pipeline...");
        try
        {
            // 1.1 Hamza Normalization
            AssertEquals("احمد", ArabicTextHelper.Normalize("أحمد"), "Hamza over Alif");
            AssertEquals("ابراهيم", ArabicTextHelper.Normalize("إبراهيم"), "Hamza under Alif");
            AssertEquals("ايه", ArabicTextHelper.Normalize("آية"), "Madda over Alif");

            // 1.2 Yaa & Alif Maqsura
            AssertEquals("لطفي", ArabicTextHelper.Normalize("لطفى"), "Alif Maqsura to Yaa");

            // 1.3 Taa Marbuta
            AssertEquals("حبيبه", ArabicTextHelper.Normalize("حبيبة"), "Taa Marbuta to Haa");

            // 1.4 Tashkeel Stripping
            AssertEquals("محمد", ArabicTextHelper.Normalize("مُحَمَّدٌ"), "Tashkeel stripping");

            // 1.5 Tatweel Stripping
            AssertEquals("ياسين", ArabicTextHelper.Normalize("يـــاســـيـــن"), "Tatweel stripping");

            // 1.6 Compound Names (عبد الرحمن vs عبدالرحمن)
            AssertEquals("عبدالرحمن", ArabicTextHelper.Normalize("عبد الرحمن"), "Compound name spacing");
            AssertEquals(ArabicTextHelper.Normalize("عبد الرحمن"), ArabicTextHelper.Normalize("عبدالرحمن"), "Compound name equivalence");

            // 1.7 Fuzzy Prefix-Insensitive Search
            AssertTrue(ArabicTextHelper.IsFuzzyMatch("محمد احمد لطفي", "احمد"), "Fuzzy match without hamza");
            AssertTrue(ArabicTextHelper.IsFuzzyMatch("محمد احمد السعيد", "سعيد"), "Prefix-insensitive match ignoring الـ");
            AssertTrue(ArabicTextHelper.IsFuzzyMatch("ياسين سمير سيد حسانين", "ياسين"), "Trainee lookup");

            // 1.8 LRM BiDi Wrapping
            string mixedText = "أحمد محمد - CPL/IR";
            string wrapped = ArabicTextHelper.WrapAviationBiDi(mixedText);
            AssertTrue(wrapped.Contains(ArabicTextHelper.LRM_STRING), "LRM presence in mixed text");
            Console.WriteLine("  ✔ All ArabicTextHelper normalization & BiDi tests PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 1 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 2: Decoupled Trainee Identity & Headcount vs Orders
        Console.WriteLine("\n[TEST SUITE 2] Testing Student Decoupling & Headcount vs Course Volume...");
        var db = new DatabaseService();
        try
        {
            await db.InitializeAsync();
            await db.ClearAllDataAsync();

            // Scenario: Ahmed enrolls in PPL (Order #1), then later enrolls in CPL/IR (Order #2)
            int studentId1 = await db.GetOrCreateStudentAsync("أحمد محمد علي", "مصري");
            int studentId2 = await db.GetOrCreateStudentAsync("احمد محمد على", "مصري"); // Variation in hamzas/yaa
            AssertEquals(studentId1, studentId2, "Deduplication: Name variations must yield identical student ID");

            // Add Order 1 (PPL - Completed)
            await db.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = studentId1,
                OrderNumber = "101",
                ProgramType = "PPL",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2025, 1, 1),
                CompletionDate = new DateTime(2025, 6, 1), // Completed!
                Year = 2025,
                SequenceNumber = 1
            });

            // Add Order 2 (CPL/IR - Active / in Cockpit)
            await db.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = studentId1,
                OrderNumber = "202",
                ProgramType = "CPL/IR",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2026, 1, 15),
                CompletionDate = null, // Active / no completion date!
                Year = 2026,
                SequenceNumber = 2
            });

            var metrics = await db.GetDashboardMetricsAsync();
            AssertEquals(1, metrics.TotalUniqueStudents, "Headcount: Must count exactly 1 unique human trainee");
            AssertEquals(2, metrics.TotalCourseEnrollments, "Operational Volume: Must count exactly 2 course orders");
            AssertEquals(1, metrics.ActiveTraineesCount, "Pipeline: Must count 1 active cockpit trainee");
            AssertEquals(0, metrics.GraduatedTraineesCount, "Pipeline: Student with an active order is NOT graduated yet");
            AssertEquals(2.0, metrics.OrdersPerStudentRatio, "Ratio: 2 orders / 1 student = 2.0");

            Console.WriteLine("  ✔ Decoupled Trainee Identity & Headcount vs Orders tests PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 2 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 3: Pipeline Automation (Active vs Completed) & 360 Trajectory
        Console.WriteLine("\n[TEST SUITE 3] Testing Pipeline Automation & 360° Trajectory...");
        try
        {
            var students = await db.GetAllStudentsAsync();
            AssertEquals(1, students.Count, "Single student in registry");
            var student = students[0];
            AssertEquals("قيد التدريب", student.OverallStatus, "Student status is Active (has open order)");

            // Complete Order 2
            var orders = await db.GetAllOrdersAsync();
            var openOrder = orders.Find(o => o.OrderNumber == "202");
            AssertTrue(openOrder != null, "Order 202 exists");
            AssertEquals("قيد التدريب", openOrder!.Status, "Order 202 status is Active");

            // Close order 202
            await db.UpdateOrderCompletionDateAsync(openOrder.Id, new DateTime(2026, 5, 20));

            // Verify student is now graduated
            var updatedStudent = await db.GetStudentWithTrajectoryAsync(student.Id);
            AssertTrue(updatedStudent != null, "Trajectory loaded");
            AssertEquals(2, updatedStudent!.Orders.Count, "Chronological trajectory count");
            AssertEquals("خريج", updatedStudent.OverallStatus, "Student is now Graduated (all orders closed)");
            AssertTrue(updatedStudent.HasPPL, "Has PPL milestone");
            AssertTrue(updatedStudent.HasCPLIR, "Has CPL/IR milestone");

            Console.WriteLine("  ✔ Pipeline Automation & 360° Trajectory tests PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 3 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 4: Real Excel Ingestion (2اوامر التدريب.xlsx)
        Console.WriteLine("\n[TEST SUITE 4] Ingesting real Excel file: 2اوامر التدريب.xlsx...");
        string excelPath = @"C:\Users\Mi5a\EAA System\2اوامر التدريب.xlsx";
        var excelSync = new ExcelSyncService(db);
        try
        {
            await db.ClearAllDataAsync();
            var syncResult = await excelSync.ImportFromWorkbookAsync(excelPath);

            Console.WriteLine($"  -> Total Rows Scanned: {syncResult.TotalRowsScanned}");
            Console.WriteLine($"  -> Total Course Orders Imported: {syncResult.OrdersImported}");
            Console.WriteLine($"  -> Total Unique Trainees (Human Headcount): {syncResult.UniqueStudentsCount}");
            Console.WriteLine($"  -> Active Orders in Cockpit: {syncResult.ActiveOrdersCount}");
            Console.WriteLine($"  -> Completed Orders: {syncResult.CompletedOrdersCount}");

            AssertTrue(syncResult.OrdersImported > 100, "Imported over 100 orders from real workbook");
            AssertTrue(syncResult.UniqueStudentsCount > 0, "Deduplicated unique students found");
            AssertTrue(syncResult.UniqueStudentsCount <= syncResult.OrdersImported, "Unique students <= Total orders");

            var finalMetrics = await db.GetDashboardMetricsAsync();
            Console.WriteLine($"\n  Executive EAA Dashboard Summary:");
            Console.WriteLine($"  --------------------------------");
            Console.WriteLine($"  - إجمالي الطلبة الفعليين: {finalMetrics.TotalUniqueStudents} طالب");
            Console.WriteLine($"  - إجمالي أوامر التدريب: {finalMetrics.TotalCourseEnrollments} أمر");
            Console.WriteLine($"  - الطلبة قيد التدريب: {finalMetrics.ActiveTraineesCount} متدرب");
            Console.WriteLine($"  - الخريجون والمتممون: {finalMetrics.GraduatedTraineesCount} خريج");
            Console.WriteLine($"  - متوسط الأوامر لكل متدرب: {finalMetrics.OrdersPerStudentRatio:F2}");

            Console.WriteLine("  ✔ Real Excel Ingestion tests PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 4 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 5: Ministerial RTL Report Generation (ClosedXML)
        Console.WriteLine("\n[TEST SUITE 5] Generating Official Ministry RTL Report...");
        try
        {
            string exportPath = Path.Combine(Environment.CurrentDirectory, "Test_Ministry_Report.xlsx");
            await excelSync.ExportOfficialMinistryReportAsync(exportPath);

            AssertTrue(File.Exists(exportPath), "Exported Excel file exists");

            using var exportedWb = new XLWorkbook(exportPath);
            var ws = exportedWb.Worksheet(1);

            // Verify RTL compatibility
            AssertTrue(ws.RightToLeft, "worksheet.RightToLeft MUST be true for native RTL layout");

            // Verify 4-line EAA Header
            string line1 = ws.Cell("A1").GetString();
            string line2 = ws.Cell("A2").GetString();
            string line3 = ws.Cell("A3").GetString();

            AssertTrue(line1.Contains("جمهورية مصر العربية"), "Line 1: Arab Republic of Egypt");
            AssertTrue(line2.Contains("وزارة الطيران المدني"), "Line 2: Ministry of Civil Aviation");
            AssertTrue(line3.Contains("إدارة التدريب"), "Line 3: EAA Training Directorate");

            // Verify Column headers at Row 6
            AssertEquals("م", ws.Cell(6, 1).GetString(), "Col 1 Header");
            AssertEquals("اسم المتدرب / الطالب", ws.Cell(6, 2).GetString(), "Col 2 Header");
            AssertEquals("رقم أمر التدريب", ws.Cell(6, 4).GetString(), "Col 4 Header");

            Console.WriteLine($"  ✔ Generated report at: {exportPath}");
            Console.WriteLine("  ✔ Official Ministry RTL Report tests PASSED!");

            // Clean up test export
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 5 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 6: Demographics Engine & Nationality Standardization
        Console.WriteLine("\n[TEST SUITE 6] Testing DemographicsEngine & Nationality Standardization...");
        try
        {
            AssertEquals(false, DemographicsEngine.ClassifyIfInternational("مصري"), "Egyptian is local");
            AssertEquals(false, DemographicsEngine.ClassifyIfInternational("مصرية"), "Egyptian feminine is local");
            AssertEquals(false, DemographicsEngine.ClassifyIfInternational("مصر"), "Egypt is local");
            AssertEquals(false, DemographicsEngine.ClassifyIfInternational(null), "Null nationality defaults to local");

            AssertEquals(true, DemographicsEngine.ClassifyIfInternational("سعودي"), "Saudi is international");
            AssertEquals(true, DemographicsEngine.ClassifyIfInternational("اماراتي"), "Emirati is international");
            AssertEquals(true, DemographicsEngine.ClassifyIfInternational("ليبي"), "Libyan is international");
            AssertEquals(true, DemographicsEngine.ClassifyIfInternational("سوداني"), "Sudanese is international");
            AssertEquals(true, DemographicsEngine.ClassifyIfInternational("عراقي"), "Iraqi is international");
            AssertEquals(true, DemographicsEngine.ClassifyIfInternational("كويتي"), "Kuwaiti is international");
            AssertEquals(true, DemographicsEngine.ClassifyIfInternational("اردني"), "Jordanian is international");

            // Standardization
            AssertEquals("سعودي", DemographicsEngine.StandardizeNationality("المملكة العربية السعودية"), "KSA standardization");
            AssertEquals("إماراتي", DemographicsEngine.StandardizeNationality("الإمارات"), "UAE standardization");
            AssertEquals("سوري", DemographicsEngine.StandardizeNationality("سوريا"), "Syria standardization");

            Console.WriteLine("  ✔ DemographicsEngine classification & standardization tests PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 6 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 7: Multi-Year Scoping, Deduplication & 5 Training Streams
        Console.WriteLine("\n[TEST SUITE 7] Testing Multi-Year Scoping & 5 Training Streams Architecture...");
        try
        {
            await db.ClearAllDataAsync();

            // Cadet 1: Local cadet doing Part 61 PPL in 2025, Part 61 CPL/IR in 2026
            int c1 = await db.GetOrCreateStudentAsync("طارق مصطفى محمود", "مصري");
            await db.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = c1,
                OrderNumber = "ORD-2025-01",
                ProgramType = "PPL",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2025, 2, 1),
                CompletionDate = new DateTime(2025, 7, 1),
                Year = 2025
            });
            await db.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = c1,
                OrderNumber = "ORD-2026-01",
                ProgramType = "CPL/IR",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2026, 1, 10),
                CompletionDate = null,
                Year = 2026
            });

            // Cadet 2: International cadet doing Part 141 Integrated Batch in 2025
            int c2 = await db.GetOrCreateStudentAsync("عبد الله الشمري", "سعودي");
            await db.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = c2,
                OrderNumber = "ORD-2025-02",
                ProgramType = "دفعة 141 المتكاملة",
                RegulationCategory = "141 (نظام دفعات)",
                EnrollmentDate = new DateTime(2025, 3, 1),
                CompletionDate = null,
                Year = 2025
            });

            // Cadet 3: International cadet doing Type Rating & Hours in 2026
            int c3 = await db.GetOrCreateStudentAsync("فيصل الحربي", "كويتي");
            await db.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = c3,
                OrderNumber = "ORD-2026-02",
                ProgramType = "Cessna 172 Type Rating & 50 Hours",
                RegulationCategory = "طراز وبناء ساعات",
                EnrollmentDate = new DateTime(2026, 2, 1),
                CompletionDate = null,
                Year = 2026
            });

            // Cadet 4: Foreign Evaluation in 2026
            int c4 = await db.GetOrCreateStudentAsync("جون مايكل", "أجنبي");
            await db.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = c4,
                OrderNumber = "ORD-2026-03",
                ProgramType = "معادلة رخصة أجنبية ECAA",
                RegulationCategory = "تقييم ومعادلة",
                EnrollmentDate = new DateTime(2026, 2, 15),
                CompletionDate = new DateTime(2026, 3, 1),
                Year = 2026
            });

            // Global Metrics (all years)
            var allMetrics = await db.GetDashboardMetricsAsync(null);
            var allDemo = await db.GetDemographicsSummaryAsync(null);
            AssertEquals(4, allMetrics.TotalUniqueStudents, "All years: 4 unique trainees");
            AssertEquals(5, allMetrics.TotalCourseEnrollments, "All years: 5 total orders");
            AssertEquals(3, allDemo.InternationalCount, "All years: 3 international trainees (Saudi, Kuwaiti, Foreign)");

            // 2025 Metrics
            var metrics2025 = await db.GetDashboardMetricsAsync(2025);
            var demo2025 = await db.GetDemographicsSummaryAsync(2025);
            AssertEquals(2, metrics2025.TotalUniqueStudents, "2025: 2 unique trainees (Tarek, Abdullah)");
            AssertEquals(2, metrics2025.TotalCourseEnrollments, "2025: 2 orders");
            AssertEquals(1, demo2025.InternationalCount, "2025: 1 international trainee");
            AssertEquals(1, demo2025.Part61Count, "2025: 1 Part 61 order");
            AssertEquals(1, demo2025.Part141Count, "2025: 1 Part 141 order");

            // 2026 Metrics
            var metrics2026 = await db.GetDashboardMetricsAsync(2026);
            var demo2026 = await db.GetDemographicsSummaryAsync(2026);
            AssertEquals(3, metrics2026.TotalUniqueStudents, "2026: 3 unique trainees (Tarek, Faisal, John)");
            AssertEquals(3, metrics2026.TotalCourseEnrollments, "2026: 3 orders");
            AssertEquals(2, demo2026.InternationalCount, "2026: 2 international trainees");

            // Verify Stream Filters
            var p61Orders = await db.GetAllOrdersAsync(regulatoryTrackFilter: "Part61");
            AssertEquals(2, p61Orders.Count, "Part 61 stream: 2 orders");

            var p141Orders = await db.GetAllOrdersAsync(regulatoryTrackFilter: "Part141");
            AssertEquals(1, p141Orders.Count, "Part 141 stream: 1 order");

            var trOrders = await db.GetAllOrdersAsync(regulatoryTrackFilter: "TypeRating");
            AssertEquals(1, trOrders.Count, "Type Rating stream: 1 order");

            var evalOrders = await db.GetAllOrdersAsync(regulatoryTrackFilter: "Evaluation");
            AssertEquals(1, evalOrders.Count, "Evaluation stream: 1 order");

            Console.WriteLine("  ✔ Multi-Year Scoping & 5 Training Streams tests PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 7 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 8: Consular International Students Roster Generation (ClosedXML)
        Console.WriteLine("\n[TEST SUITE 8] Testing Consular International Students Roster Generation...");
        try
        {
            string consularExportPath = Path.Combine(Environment.CurrentDirectory, "Test_Consular_Roster.xlsx");
            await excelSync.ExportInternationalStudentsRosterAsync(consularExportPath, 2026);

            AssertTrue(File.Exists(consularExportPath), "Consular export exists");

            using var consularWb = new XLWorkbook(consularExportPath);
            var ws = consularWb.Worksheet(1);

            AssertTrue(ws.RightToLeft, "Consular worksheet MUST be Right-To-Left");
            string header1 = ws.Cell("A1").GetString();
            AssertTrue(header1.Contains("جمهورية مصر العربية"), "Consular Line 1");
            string titleCell = ws.Cell("A4").GetString();
            AssertTrue(titleCell.Contains("كشف حصر ومتابعة الطلبة الوافدين"), "Consular Title Cell");
            AssertTrue(titleCell.Contains("2026"), "Title contains filtered academic year 2026");

            // Check columns at Row 6
            AssertEquals("م", ws.Cell(6, 1).GetString(), "Col 1 Header");
            AssertEquals("اسم المتدرب / الطالب", ws.Cell(6, 2).GetString(), "Col 2 Header");
            AssertEquals("الجنسية", ws.Cell(6, 3).GetString(), "Col 3 Header");
            AssertEquals("المسار التدريبي", ws.Cell(6, 4).GetString(), "Col 4 Header");

            // Check data rows
            string studentRow1 = ws.Cell(7, 2).GetString();
            AssertTrue(!string.IsNullOrEmpty(studentRow1), "First international student row populated");

            Console.WriteLine($"  ✔ Generated consular roster at: {consularExportPath}");
            Console.WriteLine("  ✔ Consular International Students Roster tests PASSED!");

            // Clean up
            if (File.Exists(consularExportPath)) File.Delete(consularExportPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 8 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 9: In-App Dynamic Manual Order Entry
        Console.WriteLine("\n[TEST SUITE 9] Testing In-App Manual Order Entry...");
        try
        {
            var newOrder = await db.CreateManualOrderAsync(
                studentName: "حسام الدين حسن مصطفى",
                nationality: "مصري",
                regulatoryTrack: "Part61",
                programType: "PPL",
                orderNumber: "8801",
                enrollmentDate: new DateTime(2026, 3, 1),
                completionDate: null,
                notes: "إدخال يدوي مباشر من نافذة الإدخال الذكية"
            );

            AssertTrue(newOrder != null && newOrder.Id > 0, "Created order has valid ID");
            AssertEquals("حسام الدين حسن مصطفى", newOrder!.StudentDisplayName, "Student name match");
            AssertEquals("مصري", newOrder.Nationality, "Nationality match");
            AssertEquals("8801", newOrder.OrderNumber, "Order number match");
            AssertEquals("قيد التدريب", newOrder.Status, "New order status must be Active");
            Console.WriteLine("  ✔ In-App Dynamic Manual Order Entry PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 9 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 10: Deduplication & Cross-Order Linking on Manual Entry
        Console.WriteLine("\n[TEST SUITE 10] Testing Deduplication & Cross-Order Linking...");
        try
        {
            int preCount = (await db.GetAllStudentsAsync()).Count;

            // Register second order for the same student with different spelling (همزات)
            var secondOrder = await db.CreateManualOrderAsync(
                studentName: "حسام الدين حسن مصطفي", // Alif Maqsura instead of Yaa
                nationality: "مصري",
                regulatoryTrack: "Part61",
                programType: "CPL/IR",
                orderNumber: "8802",
                enrollmentDate: new DateTime(2026, 7, 1),
                completionDate: null,
                notes: "أمر CPL/IR لنفس المتدرب"
            );

            int postCount = (await db.GetAllStudentsAsync()).Count;
            AssertEquals(preCount, postCount, "Deduplication: Total unique students count must NOT increase");

            var student = await db.GetStudentWithTrajectoryAsync(secondOrder.StudentId);
            AssertTrue(student != null, "Student found");
            AssertEquals(2, student.Orders.Count, "Student has 2 linked orders");
            Console.WriteLine("  ✔ Deduplication & Cross-Order Linking PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 10 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 11: Course Progression / 1-Click Course Completion
        Console.WriteLine("\n[TEST SUITE 11] Testing Course Progression & Completion...");
        try
        {
            var orders = await db.GetAllOrdersAsync();
            var activeOrder = orders.Find(o => o.OrderNumber == "8801");
            AssertTrue(activeOrder != null, "Found order 8801");

            var completedDate = new DateTime(2026, 8, 15);
            bool completed = await db.CompleteOrderAsync(activeOrder.Id, completedDate);
            AssertTrue(completed, "CompleteOrderAsync returned true");

            var updatedOrder = (await db.GetAllOrdersAsync()).Find(o => o.Id == activeOrder.Id);
            AssertTrue(updatedOrder != null && updatedOrder.CompletionDate.HasValue, "CompletionDate recorded");
            AssertEquals("منتهي", updatedOrder!.Status, "Order status transitioned to Completed/منتهي");

            var student = await db.GetStudentWithTrajectoryAsync(updatedOrder.StudentId);
            AssertTrue(student != null, "Student found");
            AssertEquals(1, student!.ActiveOrdersCount, "1 order still active in cockpit (Order 8802)");
            AssertEquals(1, student.CompletedOrdersCount, "1 order completed (Order 8801)");
            AssertEquals("قيد التدريب", student.OverallStatus, "Student is still in training because Order 8802 is ongoing");

            // Complete the second order as well to verify full graduation transition
            var targetOrder2 = (await db.GetAllOrdersAsync()).Find(o => o.OrderNumber == "8802");
            AssertTrue(targetOrder2 != null, "Found order 8802");
            await db.CompleteOrderAsync(targetOrder2!.Id, completedDate);

            var fullyGraduatedStudent = await db.GetStudentWithTrajectoryAsync(updatedOrder.StudentId);
            AssertEquals("خريج", fullyGraduatedStudent!.OverallStatus, "When all orders finish, student becomes Graduated/خريج");
            Console.WriteLine("  ✔ Course Progression & Completion PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 11 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 12: Hot SQLite Online Snapshot Backup
        Console.WriteLine("\n[TEST SUITE 12] Testing Hot SQLite Online Backup...");
        try
        {
            var backupService = new BackupService(db.DatabasePath);
            string? backupPath = await backupService.CreateSnapshotAsync("TestSnapshot");

            AssertTrue(!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath), "Backup file was physically created on disk");
            var fi = new FileInfo(backupPath!);
            AssertTrue(fi.Length > 0, "Backup file size > 0 bytes");
            Console.WriteLine($"  ✔ Created hot SQLite backup at: {backupPath} ({fi.Length / 1024} KB)");
            Console.WriteLine("  ✔ Hot SQLite Online Backup PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 12 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 13: Non-Destructive Soft Delete & 1-Click Restore
        Console.WriteLine("\n[TEST SUITE 13] Testing Non-Destructive Soft Delete & 1-Click Restore...");
        try
        {
            var ordersBefore = await db.GetAllOrdersAsync();
            var targetOrder = ordersBefore.Find(o => o.OrderNumber == "8802");
            AssertTrue(targetOrder != null, "Target order 8802 found");

            // 1. Soft delete (Archive)
            bool archived = await db.ArchiveOrderAsync(targetOrder!.Id);
            AssertTrue(archived, "ArchiveOrderAsync returned true");

            var activeOrdersAfterArchive = await db.GetAllOrdersAsync();
            AssertTrue(!activeOrdersAfterArchive.Exists(o => o.Id == targetOrder.Id), "Archived order excluded from active orders");

            var archivedOrders = await db.GetArchivedOrdersAsync();
            AssertTrue(archivedOrders.Exists(o => o.Id == targetOrder.Id), "Archived order present in Trash Bin / Archive list");

            // 2. Restore
            bool restored = await db.RestoreOrderAsync(targetOrder.Id);
            AssertTrue(restored, "RestoreOrderAsync returned true");

            var activeOrdersAfterRestore = await db.GetAllOrdersAsync();
            AssertTrue(activeOrdersAfterRestore.Exists(o => o.Id == targetOrder.Id), "Restored order reappears in active orders");

            var archivedAfterRestore = await db.GetArchivedOrdersAsync();
            AssertTrue(!archivedAfterRestore.Exists(o => o.Id == targetOrder.Id), "Restored order removed from Trash Bin");
            Console.WriteLine("  ✔ Non-Destructive Soft Delete & 1-Click Restore PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 13 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 14: Background Excel Mirror Engine
        Console.WriteLine("\n[TEST SUITE 14] Testing Background Excel Mirroring...");
        try
        {
            var mirrorService = new ExcelMirrorService(db, excelSync);
            await mirrorService.ExecuteMirrorSyncAsync();

            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EAA_TrainingManager");
            string mirrorPath = Path.Combine(appFolder, "EAA_Master_Mirror.xlsx");

            AssertTrue(File.Exists(mirrorPath), "Excel Mirror file created on disk");
            using var wb = new XLWorkbook(mirrorPath);
            AssertTrue(wb.Worksheets.Count > 0, "Excel mirror contains worksheets");
            var ws = wb.Worksheet(1);
            AssertTrue(ws.RightToLeft, "Excel mirror worksheet is Right-To-Left");
            AssertTrue(ws.LastRowUsed()!.RowNumber() > 1, "Excel mirror contains data rows");
            Console.WriteLine($"  ✔ Generated Excel mirror at: {mirrorPath} with {ws.LastRowUsed()!.RowNumber() - 1} rows");
            Console.WriteLine("  ✔ Background Excel Mirror Engine PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 14 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 15: Lightweight In-App Updater Engine
        Console.WriteLine("\n[TEST SUITE 15] Testing Lightweight In-App Updater Engine...");
        try
        {
            var updateService = new UpdateService();
            var checkResult = await updateService.CheckForUpdatesAsync();

            AssertTrue(checkResult != null, "CheckForUpdatesAsync returned valid result object");
            AssertTrue(checkResult.IsSuccess || checkResult.IsOffline, "Update service handled connection smoothly");
            Console.WriteLine($"  ✔ Update Check Result: Current={checkResult.CurrentVersion}, Latest={checkResult.LatestVersion}, HasUpdate={checkResult.HasUpdate}");
            Console.WriteLine("  ✔ Lightweight In-App Updater Engine PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 15 FAILED: {ex.Message}");
            failures++;
        }

        Console.WriteLine("\n===============================================================================");
        if (failures == 0)
        {
            Console.WriteLine("  ALL VERIFICATION TESTS COMPLETED WITH 100% SUCCESS!");
        }
        else
        {
            Console.WriteLine($"  VERIFICATION FAILED: {failures} test suite(s) had errors.");
        }
        Console.WriteLine("===============================================================================");

        return failures;
    }

    static void AssertEquals<T>(T expected, T actual, string description)
    {
        if (!object.Equals(expected, actual))
            throw new Exception($"Assertion failed for [{description}]: expected '{expected}', but got '{actual}'");
    }

    static void AssertTrue(bool condition, string description)
    {
        if (!condition)
            throw new Exception($"Assertion failed for [{description}]: expected true, but got false");
    }
}
