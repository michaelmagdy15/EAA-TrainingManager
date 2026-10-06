using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using EAATrainingManager.Helpers;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.Tests;

class Program
{
    private static string _testDataDirectory = string.Empty;

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

        _testDataDirectory = Path.Combine(Path.GetTempPath(), "EAA-TMS-Verification", $"run-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDataDirectory);
        AppLogService.ConfigureDirectory(Path.Combine(_testDataDirectory, "Logs"));
        string testDatabasePath = Path.Combine(_testDataDirectory, "eaa_training.db");
        Console.WriteLine($"Isolated verification data: {_testDataDirectory}");

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
        var db = new DatabaseService(testDatabasePath, allowDestructiveTestReset: true);
        try
        {
            AssertEquals(Path.GetFullPath(testDatabasePath), db.DatabasePath, "Test database uses its isolated path");
            string productionDatabasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EAA_TrainingManager", "eaa_training.db");
            AssertTrue(!string.Equals(db.DatabasePath, Path.GetFullPath(productionDatabasePath), StringComparison.OrdinalIgnoreCase), "Test database differs from the production database path");
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
        string excelPath = FindWorkbookFixture();
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
            string exportPath = Path.Combine(_testDataDirectory, "Test_Ministry_Report.xlsx");
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
            string consularExportPath = Path.Combine(_testDataDirectory, "Test_Consular_Roster.xlsx");
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
            AssertEquals(2, student!.Orders.Count, "Student has 2 linked orders");
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
            bool completed = await db.CompleteOrderAsync(activeOrder!.Id, completedDate);
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
            var backupService = new BackupService(db.DatabasePath, Path.Combine(_testDataDirectory, "Backups"));
            string? backupPath = await backupService.CreateSnapshotAsync("TestSnapshot");

            AssertTrue(!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath), "Backup file was physically created on disk");
            var fi = new FileInfo(backupPath!);
            AssertTrue(fi.Length > 0, "Backup file size > 0 bytes");
            Console.WriteLine($"  ✔ Created hot SQLite backup at: {backupPath} ({fi.Length / 1024} KB)");

            var studentsBeforeRestore = await db.GetAllStudentsAsync();
            string retainedStudentName = studentsBeforeRestore[0].DisplayName;
            int postSnapshotStudentId = await db.GetOrCreateStudentAsync("Post Snapshot Recovery Probe");
            AssertTrue(postSnapshotStudentId > 0, "Post-snapshot change was written before restore");
            AssertTrue(await backupService.RestoreSnapshotAsync(backupPath!), "Snapshot restore completes successfully");
            var studentsAfterRestore = await db.GetAllStudentsAsync();
            AssertEquals(studentsBeforeRestore.Count, studentsAfterRestore.Count, "Restore returns the database to its snapshot state");
            AssertTrue(studentsAfterRestore.Exists(s => s.DisplayName == retainedStudentName), "Pre-snapshot student data survives restore");
            AssertTrue(!studentsAfterRestore.Exists(s => s.DisplayName == "Post Snapshot Recovery Probe"), "Post-snapshot mutation is removed by restore");
            string corruptSnapshotPath = Path.Combine(_testDataDirectory, "corrupt-snapshot.db");
            await File.WriteAllTextAsync(corruptSnapshotPath, "not a SQLite database");
            AssertTrue(!await backupService.RestoreSnapshotAsync(corruptSnapshotPath), "Invalid snapshot is rejected");
            AssertEquals(studentsBeforeRestore.Count, (await db.GetAllStudentsAsync()).Count, "Rejected restore leaves the recovered database intact");
            Console.WriteLine("  ✔ Hot SQLite Online Backup PASSED!");
            Console.WriteLine("  ✔ Snapshot restore recovered the prior database state!");
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
            var mirrorService = new ExcelMirrorService(db, excelSync, _testDataDirectory, copyToDesktop: false);
            await mirrorService.ExecuteMirrorSyncAsync();

            string mirrorPath = Path.Combine(_testDataDirectory, "EAA_Master_Mirror.xlsx");

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
            AssertTrue(checkResult!.IsSuccess || checkResult.IsOffline, "Update service handled connection smoothly");
            Console.WriteLine($"  ✔ Update Check Result: Current={checkResult.CurrentVersion}, Latest={checkResult.LatestVersion}, HasUpdate={checkResult.HasUpdate}");
            Console.WriteLine("  ✔ Lightweight In-App Updater Engine PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 15 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 16: Scheduling Conflict Prevention & Daily Operations Board
        Console.WriteLine("\n[TEST SUITE 16] Testing Training Session Scheduling & Conflict Prevention...");
        try
        {
            await db.ClearAllDataAsync();
            int studentA = await db.GetOrCreateStudentAsync("Scheduler Student A");
            int studentB = await db.GetOrCreateStudentAsync("Scheduler Student B");
            int studentC = await db.GetOrCreateStudentAsync("Scheduler Student C");
            await db.SaveAircraftResourceAsync(new AircraftResource
            {
                Registration = "SU-EAA",
                ResourceType = "Aircraft",
                AircraftType = "C172",
                Status = "Available",
                HobbsHours = 100
            });
            await db.SaveAircraftResourceAsync(new AircraftResource
            {
                Registration = "SU-MNT",
                ResourceType = "Aircraft",
                AircraftType = "C172",
                Status = "Available",
                HobbsHours = 200,
                MaintenanceDueAtHours = 200
            });
            AssertTrue(await db.IsResourceDispatchableAsync("SU-EAA"), "Available aircraft is dispatchable");
            AssertTrue(!await db.IsResourceDispatchableAsync("SU-MNT"), "Maintenance-due aircraft is blocked");
            int expiredMedicalId = await db.SaveComplianceRecordAsync(new ComplianceRecord
            {
                StudentId = studentC,
                RecordType = "Medical",
                IssuedAt = DateTime.Today.AddYears(-1),
                ExpiresAt = DateTime.Today.AddDays(-1),
                IsVerified = true
            });
            AssertTrue(expiredMedicalId > 0, "Expired medical record is saved");
            AssertTrue(await db.HasExpiredVerifiedComplianceAsync(studentC), "Expired verified medical is detected");
            var start = DateTime.Today.AddHours(9);
            var end = start.AddHours(2);

            int resourceAvailableWindow = await db.SaveAvailabilityWindowAsync(new AvailabilityWindow
            {
                ResourceType = "Resource",
                ResourceName = "SU-EAA",
                StartAt = start,
                EndAt = end,
                AvailabilityState = "Available",
                Reason = "Scheduled fleet capacity"
            });
            int instructorBlackout = await db.SaveAvailabilityWindowAsync(new AvailabilityWindow
            {
                ResourceType = "Instructor",
                ResourceName = "CFI One",
                StartAt = start.AddMinutes(30),
                EndAt = start.AddMinutes(60),
                AvailabilityState = "Unavailable",
                Reason = "Instructor unavailable"
            });

            bool blackoutBlocked = false;
            try
            {
                await db.ScheduleTrainingSessionAsync(new TrainingSession
                {
                    StudentId = studentA,
                    LessonTitle = "Blackout Blocked Flight",
                    InstructorName = "CFI One",
                    ResourceName = "SU-EAA",
                    StartAt = start,
                    EndAt = end
                });
            }
            catch (InvalidOperationException) { blackoutBlocked = true; }
            AssertTrue(blackoutBlocked, "Instructor blackout blocks an overlapping schedule request");
            AssertTrue(await db.ArchiveAvailabilityWindowAsync(instructorBlackout), "Authorized operator can archive a blackout window");

            int weatherWindowId = await db.SaveAvailabilityWindowAsync(new AvailabilityWindow
            {
                ResourceType = "Weather",
                ResourceName = "October Airport",
                StartAt = start,
                EndAt = end,
                AvailabilityState = "Unavailable",
                Reason = "Operator-entered weather restriction"
            });
            bool weatherBlocked = false;
            try
            {
                await db.ScheduleTrainingSessionAsync(new TrainingSession
                {
                    StudentId = studentA,
                    LessonTitle = "Weather Restricted Flight",
                    ResourceName = "SU-EAA",
                    Location = "October Airport",
                    StartAt = start,
                    EndAt = end
                });
            }
            catch (InvalidOperationException) { weatherBlocked = true; }
            AssertTrue(weatherBlocked && weatherWindowId > 0, "Operator-entered weather restrictions block schedule creation");
            AssertTrue(await db.ArchiveAvailabilityWindowAsync(weatherWindowId), "Weather restriction can be archived after expiry or review");

            bool outsideAvailabilityBlocked = false;
            try
            {
                await db.ScheduleTrainingSessionAsync(new TrainingSession
                {
                    StudentId = studentB,
                    LessonTitle = "Outside Availability Window",
                    ResourceName = "SU-EAA",
                    StartAt = end.AddHours(2),
                    EndAt = end.AddHours(3)
                });
            }
            catch (InvalidOperationException) { outsideAvailabilityBlocked = true; }
            AssertTrue(outsideAvailabilityBlocked, "Configured available windows constrain schedulable resource time");

            int sessionId = await db.ScheduleTrainingSessionAsync(new TrainingSession
            {
                StudentId = studentA,
                RegulatoryTrack = "Part141",
                LessonTitle = "Stage 1 Flight",
                InstructorName = "CFI One",
                ResourceName = "SU-EAA",
                Location = "October Airport",
                StartAt = start,
                EndAt = end
            });
            AssertTrue(sessionId > 0, "Session was scheduled");
            AssertTrue(resourceAvailableWindow > 0, "Resource availability window was persisted");

            var sessions = await db.GetTrainingSessionsAsync(DateTime.Today);
            AssertEquals(1, sessions.Count, "Daily schedule returns the session");
            AssertEquals("Scheduled", sessions[0].Status, "New session is scheduled");
            var queuedNotifications = await db.GetPendingNotificationOutboxAsync();
            var studentScheduleNotification = queuedNotifications.Find(n => n.EntityId == sessionId && n.EventType == "BookingCreated" && n.RecipientType == "Student");
            AssertTrue(studentScheduleNotification != null, "Schedule creation queues a student notification event");
            AssertTrue(await db.RecordNotificationAttemptAsync(studentScheduleNotification!.Id, false, "Offline test delivery"), "Notification failure is recorded for retry");
            var failedNotification = (await db.GetPendingNotificationOutboxAsync()).Find(n => n.Id == studentScheduleNotification.Id);
            AssertTrue(failedNotification?.Status == "Failed" && failedNotification.AttemptCount == 1, "Failed notification remains visible in the outbox");
            AssertTrue(await db.RecordNotificationAttemptAsync(studentScheduleNotification.Id, true), "Notification can be marked delivered after retry");

            bool conflictDetected = false;
            try
            {
                await db.ScheduleTrainingSessionAsync(new TrainingSession
                {
                    StudentId = studentB,
                    LessonTitle = "Conflicting Flight",
                    InstructorName = "CFI One",
                    ResourceName = "SU-EAB",
                    StartAt = start.AddMinutes(30),
                    EndAt = end.AddMinutes(30)
                });
            }
            catch (InvalidOperationException)
            {
                conflictDetected = true;
            }
            AssertTrue(conflictDetected, "Instructor overlap is rejected");

            bool maintenanceBlockDetected = false;
            try
            {
                await db.ScheduleTrainingSessionAsync(new TrainingSession
                {
                    StudentId = studentB,
                    LessonTitle = "Maintenance Blocked Flight",
                    ResourceName = "SU-MNT",
                    StartAt = end.AddHours(1),
                    EndAt = end.AddHours(2)
                });
            }
            catch (InvalidOperationException)
            {
                maintenanceBlockDetected = true;
            }
            AssertTrue(maintenanceBlockDetected, "Maintenance-due aircraft cannot be scheduled");

            bool complianceBlockDetected = false;
            try
            {
                await db.ScheduleTrainingSessionAsync(new TrainingSession
                {
                    StudentId = studentC,
                    LessonTitle = "Compliance Blocked Flight",
                    StartAt = end.AddHours(3),
                    EndAt = end.AddHours(4)
                });
            }
            catch (InvalidOperationException)
            {
                complianceBlockDetected = true;
            }
            AssertTrue(complianceBlockDetected, "Expired compliance blocks scheduling");

            int rescheduledSessionId = await db.ScheduleTrainingSessionAsync(new TrainingSession
            {
                StudentId = studentB,
                LessonTitle = "Reschedule Test",
                InstructorName = "CFI Two",
                ResourceName = "SU-EAB",
                StartAt = start.AddHours(3),
                EndAt = end.AddHours(3)
            });
            bool rescheduleConflictDetected = false;
            try
            {
                await db.RescheduleTrainingSessionAsync(rescheduledSessionId, start.AddMinutes(30), end.AddMinutes(30), "CFI One", "SU-EAB", string.Empty, "Conflict check");
            }
            catch (InvalidOperationException) { rescheduleConflictDetected = true; }
            AssertTrue(rescheduleConflictDetected, "Rescheduling re-runs student/instructor/resource/room conflict checks");
            AssertTrue(await db.RescheduleTrainingSessionAsync(rescheduledSessionId, start.AddHours(4), end.AddHours(4), "CFI Two", "SU-EAB", "October Airport", "Instructor availability changed"), "Valid reschedule is saved with a reason");
            var rescheduleAudit = await db.GetAuditEventsAsync("TrainingSession", rescheduledSessionId);
            var rescheduleVersion = rescheduleAudit.Find(e => e.Action == "Rescheduled");
            AssertTrue(rescheduleVersion?.VersionNo == 2 && rescheduleVersion.BeforeJson != null && rescheduleVersion.AfterJson?.Contains("Instructor availability changed", StringComparison.Ordinal) == true, "Reschedule captures reason and before/after history");

            var flightOperations = new FlightOperationsService(db);
            AssertTrue(await flightOperations.TransitionAsync(sessionId, "Confirmed"), "Scheduled booking can be confirmed");
            bool directReleaseBypassDenied = false;
            try { await db.UpdateTrainingSessionStatusAsync(sessionId, "Released"); }
            catch (InvalidOperationException) { directReleaseBypassDenied = true; }
            AssertTrue(directReleaseBypassDenied, "Dispatch release cannot bypass the release checklist service");
            bool incompleteReleaseBlocked = false;
            try
            {
                await flightOperations.ReleaseAsync(sessionId, new DispatchReleaseRequest("Weather checked", "FIF-TEST-001", "Test release reason", new Dictionary<string, bool>()));
            }
            catch (InvalidOperationException)
            {
                incompleteReleaseBlocked = true;
            }
            AssertTrue(incompleteReleaseBlocked, "Dispatch release rejects an incomplete preflight checklist");
            int releaseId = await flightOperations.ReleaseAsync(sessionId, new DispatchReleaseRequest(
                "Operational weather briefing reviewed",
                "FIF-TEST-001",
                "Dispatch reviewed the flight package and resource status.",
                FlightOperationsService.RequiredReleaseChecks.ToDictionary(key => key, _ => true)));
            AssertTrue(releaseId > 0, "Confirmed booking can be released with completed release evidence");
            AssertTrue(await flightOperations.TransitionAsync(sessionId, "Airborne"), "Released session transitions airborne");
            AssertTrue(await flightOperations.TransitionAsync(sessionId, "Landed"), "Airborne session transitions landed");
            var linkedFlight = new FlightRecord
            {
                TrainingSessionId = sessionId,
                StudentId = studentA,
                ActivityType = "Dual",
                ResourceName = "SU-EAA",
                InstructorName = "CFI One",
                Route = "HECA local area",
                StartAt = start,
                EndAt = end,
                HobbsStart = 100,
                HobbsEnd = 101,
                Landings = 3
            };
            int linkedFlightId = await db.RecordFlightAsync(linkedFlight);
            AssertTrue(linkedFlightId > 0, "Landed dispatch session completes from its linked flight record");
            AssertEquals(linkedFlightId, await db.RecordFlightAsync(linkedFlight), "Retrying flight completion returns the original record ID");
            AssertEquals(1, (await db.GetFlightRecordsAsync(DateTime.Today, studentA)).Count(r => r.TrainingSessionId == sessionId), "Retry does not duplicate the flight record");
            sessions = await db.GetTrainingSessionsAsync(DateTime.Today);
            AssertEquals("Completed", sessions[0].Status, "Completed status persists");
            var sessionAudit = await db.GetAuditEventsAsync("TrainingSession", sessionId);
            AssertTrue(sessionAudit.Exists(e => e.Action == "Created"), "Session creation is audited");
            AssertTrue(sessionAudit.Exists(e => e.Action == "Released"), "Dispatch release is audited");
            AssertTrue(sessionAudit.Exists(e => e.Action == "CompletedFromRecord"), "Post-flight completion is linked and audited");

            int cancelledSessionId = await db.ScheduleTrainingSessionAsync(new TrainingSession
            {
                StudentId = studentA,
                LessonTitle = "Cancellation Test",
                StartAt = start.AddHours(3),
                EndAt = end.AddHours(3)
            });
            AssertTrue(await flightOperations.ResolveAsync(cancelledSessionId, "Cancelled", "Weather restriction test"), "Cancellation records a reason and transitions the booking");
            int noShowSessionId = await db.ScheduleTrainingSessionAsync(new TrainingSession
            {
                StudentId = studentA,
                LessonTitle = "No-show Test",
                StartAt = start.AddHours(5),
                EndAt = end.AddHours(5)
            });
            AssertTrue(await flightOperations.ResolveAsync(noShowSessionId, "NoShow", "Trainee did not report"), "No-show records a reason and transitions the booking");
            using (var exceptionsConnection = await db.OpenIdentityConnectionAsync())
            {
                using var exceptionsCommand = exceptionsConnection.CreateCommand();
                exceptionsCommand.CommandText = "SELECT COUNT(*) FROM SessionExceptions WHERE TrainingSessionId IN (@cancelled, @noShow);";
                exceptionsCommand.Parameters.AddWithValue("@cancelled", cancelledSessionId);
                exceptionsCommand.Parameters.AddWithValue("@noShow", noShowSessionId);
                AssertEquals(2L, (long)(await exceptionsCommand.ExecuteScalarAsync())!, "Cancellation and no-show reasons persist as separate operational records");
            }
            var remainingNotifications = await db.GetPendingNotificationOutboxAsync(1000);
            AssertTrue(remainingNotifications.Exists(n => n.EntityId == sessionId && n.EventType == "DispatchReleased"), "Dispatch release queues a notification event");
            AssertTrue(remainingNotifications.Exists(n => n.EntityId == sessionId && n.EventType == "FlightCompleted"), "Post-flight completion queues a notification event");
            AssertTrue(remainingNotifications.Exists(n => n.EntityId == rescheduledSessionId && n.EventType == "ScheduleChanged"), "Reschedule queues a notification event");
            AssertTrue(remainingNotifications.Exists(n => n.EntityId == cancelledSessionId && n.EventType == "BookingCancelled"), "Cancellation queues a notification event");
            AssertTrue(remainingNotifications.Exists(n => n.EntityId == noShowSessionId && n.EventType == "BookingNoShow"), "No-show queues a notification event");
            var operationsMetrics = await db.GetFlightOperationsMetricsAsync(DateTime.Today, DateTime.Today.AddDays(1));
            AssertTrue(operationsMetrics.Completed >= 1 && operationsMetrics.Cancelled == 1 && operationsMetrics.NoShow == 1, "Operations dashboard counts flight lifecycle, cancellation, and no-show outcomes");
            AssertTrue(Math.Abs(operationsMetrics.CompletedFlightHours - 2.0) < 0.01, "Operations dashboard reconciles completed flight hours");
            AssertTrue(operationsMetrics.CancellationReasons.Count >= 2, "Operations dashboard exposes recorded cancellation/no-show reasons");
            Console.WriteLine("  ✓ Training session scheduling & conflict prevention PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 16 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 17: Electronic Flight Records & Fleet Utilization Evidence
        Console.WriteLine("\n[TEST SUITE 17] Testing Flight Records & Aircraft Hour Updates...");
        try
        {
            await db.ClearAllDataAsync();
            int studentId = await db.GetOrCreateStudentAsync("Flight Record Student");
            await db.SaveAircraftResourceAsync(new AircraftResource { Registration = "SU-LOG", ResourceType = "Aircraft", AircraftType = "C172", Status = "Available", HobbsHours = 100 });
            var start = DateTime.Today.AddHours(11);
            int recordId = await db.RecordFlightAsync(new FlightRecord { StudentId = studentId, ActivityType = "Dual", ResourceName = "SU-LOG", InstructorName = "CFI Log", Route = "HECA local area", StartAt = start, EndAt = start.AddHours(1.2), HobbsStart = 100, HobbsEnd = 101.2, Landings = 3 });
            AssertTrue(recordId > 0, "Flight record was saved");
            var flightRecords = await db.GetFlightRecordsAsync(DateTime.Today, studentId);
            AssertEquals(1, flightRecords.Count, "Flight record is returned by day and trainee");
            AssertEquals(3, flightRecords[0].Landings, "Landing count persists");
            AssertTrue(Math.Abs(flightRecords[0].DurationHours - 1.2) < 0.001, "Flight duration is calculated");
            var resource = (await db.GetAircraftResourcesAsync()).Find(r => r.Registration == "SU-LOG");
            AssertTrue(resource != null && Math.Abs(resource.HobbsHours - 101.2) < 0.001, "Flight record advances aircraft Hobbs hours");
            var recordAudit = await db.GetAuditEventsAsync("FlightRecord", recordId);
            AssertTrue(recordAudit.Exists(e => e.Action == "Created"), "Flight record creation is audited");
            Console.WriteLine("  ✓ Flight records & fleet utilization evidence PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 17 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 18: Assessment, Stage Check, and Gate Evidence
        Console.WriteLine("\n[TEST SUITE 18] Testing Assessment & Stage-Check Evidence...");
        try
        {
            await db.ClearAllDataAsync();
            int studentId = await db.GetOrCreateStudentAsync("Assessment Student");
            int assessmentId = await db.SaveTrainingAssessmentAsync(new TrainingAssessment { StudentId = studentId, AssessmentType = "StageCheck", Title = "Stage 1 Check", ExaminerName = "Chief CFI", AttemptNumber = 1, Result = "Conditional", Deficiencies = "Crosswind control", RemedialPlan = "Two dual circuits", NextAction = "Recheck" });
            AssertTrue(assessmentId > 0, "Assessment was saved");
            var assessments = await db.GetTrainingAssessmentsAsync(studentId);
            AssertEquals(1, assessments.Count, "Assessment is returned for trainee");
            AssertEquals("Conditional", assessments[0].Result, "Assessment result persists");
            AssertEquals("Recheck", assessments[0].NextAction, "Next action persists");
            var assessmentAudit = await db.GetAuditEventsAsync("TrainingAssessment", assessmentId);
            AssertTrue(assessmentAudit.Exists(e => e.Action == "Created"), "Assessment creation is audited");
            Console.WriteLine("  ✓ Assessment & stage-check evidence PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 18 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 19: Legacy SQLite Schema Upgrade and Data Preservation
        Console.WriteLine("\n[TEST SUITE 19] Testing Legacy SQLite Migration & Data Preservation...");
        try
        {
            string legacyPath = Path.Combine(_testDataDirectory, "legacy", "eaa_training.db");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = legacyPath,
                Pooling = false
            }.ToString()))
            {
                await legacyConnection.OpenAsync();
                using var legacyCommand = legacyConnection.CreateCommand();
                legacyCommand.CommandText = @"
                    CREATE TABLE Students (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        NormalizedName TEXT NOT NULL UNIQUE,
                        DisplayName TEXT NOT NULL,
                        Nationality TEXT NOT NULL DEFAULT 'مصري',
                        NationalId TEXT,
                        Phone TEXT,
                        CreatedAt TEXT NOT NULL
                    );
                    CREATE TABLE TrainingOrders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        StudentId INTEGER NOT NULL,
                        OrderNumber TEXT NOT NULL,
                        ProgramType TEXT NOT NULL,
                        Milestone TEXT NOT NULL,
                        RegulationCategory TEXT NOT NULL,
                        EnrollmentDate TEXT,
                        CompletionDate TEXT,
                        Notes TEXT,
                        Status TEXT NOT NULL,
                        Year INTEGER NOT NULL,
                        SequenceNumber INTEGER NOT NULL,
                        FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
                    );
                    INSERT INTO Students (Id, NormalizedName, DisplayName, Nationality, CreatedAt)
                    VALUES (41, 'legacytrainee', 'Legacy Trainee', 'مصري', '2022-01-01');
                    INSERT INTO TrainingOrders (Id, StudentId, OrderNumber, ProgramType, Milestone, RegulationCategory, Status, Year, SequenceNumber)
                    VALUES (73, 41, 'LEGACY-2022-01', 'PPL', 'PPL', '61 (نظام حر)', 'قيد التدريب', 2022, 1);
                ";
                await legacyCommand.ExecuteNonQueryAsync();
            }

            var legacyDb = new DatabaseService(legacyPath);
            await legacyDb.InitializeAsync();
            var migratedStudents = await legacyDb.GetAllStudentsAsync();
            var migratedOrders = await legacyDb.GetAllOrdersAsync();
            AssertEquals(1, migratedStudents.Count, "Legacy student row preserved");
            AssertEquals(41, migratedStudents[0].Id, "Legacy student identity preserved");
            AssertEquals(1, migratedOrders.Count, "Legacy training order preserved");
            AssertEquals(73, migratedOrders[0].Id, "Legacy order identity preserved");
            AssertEquals(2026, migratedOrders[0].AcademicYear, "New academic year column receives its migration default");
            AssertEquals("Part61", migratedOrders[0].RegulatoryTrack, "New regulatory track column receives its migration default");
            using (var versionConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = legacyPath, Pooling = false }.ToString()))
            {
                await versionConnection.OpenAsync();
                using var versionCommand = versionConnection.CreateCommand();
                versionCommand.CommandText = "PRAGMA user_version;";
                AssertEquals(13L, (long)(await versionCommand.ExecuteScalarAsync())!, "Successful upgrade records the current schema version");
                versionCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
                using (var tableReader = await versionCommand.ExecuteReaderAsync())
                {
                    var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    while (await tableReader.ReadAsync()) tables.Add(tableReader.GetString(0));
                    string[] expectedTables = ["Students", "TrainingOrders", "Part141Batches", "ETPBatches", "TrainingSessions", "AircraftResources", "AuditEvents", "ComplianceRecords", "FlightRecords", "TrainingAssessments", "PersonnelRecords", "Users", "Roles", "Permissions", "UserRoles", "RolePermissions", "Locations", "UserLocations", "UserSessions", "ApprovalRecords", "ApprovalUses", "DispatchReleases", "SessionExceptions", "AvailabilityWindows", "NotificationOutbox", "RegulatoryFrameworks", "RegulatoryDocuments", "CurriculumTemplates", "CurriculumVersions", "ProgramApprovals", "CurriculumLessons", "TrainingObjectives", "ObjectivePrerequisites", "RequirementRules", "ComplianceEvidence", "RegulatoryExceptions", "ObjectiveProgress", "TrainingSessionObjectives", "RemedialPlans", "StageChecks"];
                    AssertTrue(expectedTables.All(tables.Contains), "Schema table inventory is present after upgrade");
                    AssertEquals(expectedTables.Length, tables.Count, "Schema contains only the inventoried application tables");
                }
                versionCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%';";
                AssertEquals(53L, (long)(await versionCommand.ExecuteScalarAsync())!, "Schema contains the inventoried explicit indexes");
                versionCommand.CommandText = "PRAGMA foreign_key_list(TrainingOrders);";
                using (var foreignKeyReader = await versionCommand.ExecuteReaderAsync())
                {
                    AssertTrue(await foreignKeyReader.ReadAsync(), "TrainingOrders has a student foreign key");
                    AssertEquals("Students", foreignKeyReader.GetString(2), "TrainingOrders references Students");
                    AssertEquals("CASCADE", foreignKeyReader.GetString(6), "TrainingOrders retains its documented delete action");
                }
            }
            Console.WriteLine("  ✔ Legacy schema upgraded and existing student/order rows preserved!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 19 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 20: Transactional Migration Failure and Diagnostics
        Console.WriteLine("\n[TEST SUITE 20] Testing Migration Rollback & Structured Failure Logging...");
        try
        {
            string brokenDatabasePath = Path.Combine(_testDataDirectory, "broken-migration", "eaa_training.db");
            Directory.CreateDirectory(Path.GetDirectoryName(brokenDatabasePath)!);
            using (var brokenConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = brokenDatabasePath,
                Pooling = false
            }.ToString()))
            {
                await brokenConnection.OpenAsync();
                using var brokenCommand = brokenConnection.CreateCommand();
                brokenCommand.CommandText = @"
                    CREATE TABLE Students (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        NormalizedName TEXT NOT NULL UNIQUE,
                        DisplayName TEXT NOT NULL,
                        Nationality TEXT NOT NULL DEFAULT 'مصري',
                        NationalId TEXT,
                        Phone TEXT,
                        CreatedAt TEXT NOT NULL
                    );
                    CREATE TABLE TrainingOrders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        StudentId INTEGER NOT NULL,
                        OrderNumber TEXT NOT NULL,
                        ProgramType TEXT NOT NULL,
                        Milestone TEXT NOT NULL,
                        RegulationCategory TEXT NOT NULL,
                        EnrollmentDate TEXT,
                        CompletionDate TEXT,
                        Status TEXT NOT NULL,
                        Year INTEGER NOT NULL,
                        SequenceNumber INTEGER NOT NULL,
                        FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
                    );
                ";
                await brokenCommand.ExecuteNonQueryAsync();
            }

            bool initializationFailed = false;
            string correlationId = string.Empty;
            try
            {
                await new DatabaseService(brokenDatabasePath).InitializeAsync();
            }
            catch (InvalidOperationException ex)
            {
                initializationFailed = true;
                int marker = ex.Message.LastIndexOf("Correlation ID: ", StringComparison.Ordinal);
                if (marker >= 0)
                    correlationId = ex.Message[(marker + "Correlation ID: ".Length)..].Trim();
            }

            AssertTrue(initializationFailed, "Invalid legacy schema fails visibly instead of being ignored");
            AssertTrue(!string.IsNullOrWhiteSpace(correlationId), "Failure exposes a correlation ID for diagnostics");

            using (var verifyConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = brokenDatabasePath,
                Pooling = false
            }.ToString()))
            {
                await verifyConnection.OpenAsync();
                var studentColumns = await ReadSqliteColumnsAsync(verifyConnection, "Students");
                var orderColumns = await ReadSqliteColumnsAsync(verifyConnection, "TrainingOrders");
                AssertTrue(!studentColumns.Contains("IsInternational"), "Failed migration rolls back added student columns");
                AssertTrue(!orderColumns.Contains("AcademicYear") && !orderColumns.Contains("BatchId"), "Failed migration rolls back added order columns");
                using var indexCommand = verifyConnection.CreateCommand();
                indexCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_orders_track';";
                AssertEquals(0L, (long)(await indexCommand.ExecuteScalarAsync())!, "Failed migration rolls back new indexes");
                indexCommand.CommandText = "PRAGMA user_version;";
                AssertEquals(0L, (long)(await indexCommand.ExecuteScalarAsync())!, "Failed migration does not advance the schema version");
            }

            string logPath = Path.Combine(_testDataDirectory, "Logs", "application.jsonl");
            AssertTrue(File.Exists(logPath), "Failure creates a durable application log");
            using var logDocument = System.Text.Json.JsonDocument.Parse(File.ReadLines(logPath).Last());
            var logEntry = logDocument.RootElement;
            AssertEquals("Database.Initialize", logEntry.GetProperty("operation").GetString(), "Log identifies failed operation");
            AssertEquals("SQLiteDatabase", logEntry.GetProperty("entityType").GetString(), "Log identifies affected entity type");
            AssertEquals(correlationId, logEntry.GetProperty("correlationId").GetString(), "Log correlation ID matches user-facing failure");
            AssertTrue(!string.IsNullOrWhiteSpace(logEntry.GetProperty("user").GetString()), "Log identifies the OS user");
            AssertTrue(!string.IsNullOrWhiteSpace(logEntry.GetProperty("sessionId").GetString()), "Log identifies the application session");

            using (var triggerConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()))
            {
                await triggerConnection.OpenAsync();
                using var triggerCommand = triggerConnection.CreateCommand();
                triggerCommand.CommandText = @"
                    CREATE TRIGGER RejectDiagnosticAudit
                    BEFORE INSERT ON AuditEvents
                    BEGIN
                        SELECT RAISE(ABORT, 'verification write failure');
                    END;
                ";
                await triggerCommand.ExecuteNonQueryAsync();
            }

            bool writeFailureSurfaced = false;
            try
            {
                await db.RecordAuditEventAsync("DiagnosticProbe", 912, "Create", "Intentional verification failure");
            }
            catch (InvalidOperationException)
            {
                writeFailureSurfaced = true;
            }
            AssertTrue(writeFailureSurfaced, "Database write failure is surfaced to its caller");
            using (var writeLogDocument = System.Text.Json.JsonDocument.Parse(File.ReadLines(logPath).Last()))
            {
                var writeLog = writeLogDocument.RootElement;
                AssertEquals("AuditEvent.Create", writeLog.GetProperty("operation").GetString(), "Write log identifies the failed operation");
                AssertEquals("AuditEvent", writeLog.GetProperty("entityType").GetString(), "Write log identifies the affected entity");
                AssertEquals("DiagnosticProbe:912", writeLog.GetProperty("entityId").GetString(), "Write log identifies the affected record");
            AssertTrue(!string.IsNullOrWhiteSpace(writeLog.GetProperty("correlationId").GetString()), "Write log includes its correlation ID");
            }

            string protectedDatabasePath = Path.Combine(_testDataDirectory, "protected-database", "eaa_training.db");
            var protectedDb = new DatabaseService(protectedDatabasePath);
            await protectedDb.InitializeAsync();
            await protectedDb.GetOrCreateStudentAsync("Protected Database Fixture");
            bool destructiveResetDenied = false;
            try { await protectedDb.ClearAllDataAsync(); }
            catch (UnauthorizedAccessException) { destructiveResetDenied = true; }
            AssertTrue(destructiveResetDenied, "Database reset is disabled unless the instance is explicitly test-only");
            AssertEquals(1, (await protectedDb.GetAllStudentsAsync()).Count, "Rejected reset preserves stored records");

            AppLogService.LogException("Test.LogRotation", new InvalidOperationException(new string('x', 5 * 1024 * 1024)));
            AppLogService.LogException("Test.AfterRotation", new InvalidOperationException("rotation verification"));
            AssertTrue(File.Exists(logPath + ".1"), "Oversized application log rotates to a retained numbered file");
            AssertTrue(File.ReadAllText(logPath).Contains("Test.AfterRotation", StringComparison.Ordinal), "New log events continue in the active file after rotation");
            Console.WriteLine("  ✔ Failed migration rolled back; structured logs were attributed and rotated!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 20 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 21: Identity, Permission Matrix, Sessions, and Immutable Audit
        Console.WriteLine("\n[TEST SUITE 21] Testing Identity, Permissions, Sessions & Audit Immutability...");
        try
        {
            string identityDatabasePath = Path.Combine(_testDataDirectory, "identity", "eaa_training.db");
            var identityDatabase = new DatabaseService(identityDatabasePath);
            await identityDatabase.InitializeAsync();
            var identityService = new IdentityService(identityDatabase);
            identityDatabase.AttachIdentityService(identityService);
            await identityService.EnsureAuthorizationCatalogAsync();

            var locations = await identityService.GetLocationsAsync();
            AssertEquals(4, locations.Count, "Configured EAA operating locations are persisted");
            int administratorId = await identityService.CreateBootstrapAdministratorAsync("eaa-admin", "EAA Administrator", "Initial-Admin-Password-2026!");
            AssertTrue(administratorId > 0, "First administrator can be bootstrapped without a source-stored password");

            AssertEquals<UserSession?>(null, await identityService.AuthenticateAsync("eaa-admin", "incorrect-password"), "Incorrect password is denied");
            var administratorSession = await identityService.AuthenticateAsync("eaa-admin", "Initial-Admin-Password-2026!", "OCT");
            AssertTrue(administratorSession != null, "Valid administrator login creates a session");
            AssertEquals("OCT", administratorSession!.LocationCode, "Session is scoped to selected location");
            AssertTrue(await identityService.HasPermissionAsync(administratorSession.SessionId, "users", PermissionLevel.Approve), "Administrator has user-approval permission");
            int configuredLocationId = await identityService.CreateLocationAsync(administratorSession.SessionId, "TEST", "Test Operating Location / موقع الاختبار");
            AssertTrue(configuredLocationId > 0, "Authorized administrator can configure an additional operating location");
            AssertEquals(5, (await identityService.GetAvailableLocationsAsync("eaa-admin")).Count, "New location is assigned to active administrators");
            AssertTrue(await identityService.SetLocationActiveAsync(administratorSession.SessionId, configuredLocationId, false), "Administrator can deactivate a location");
            AssertEquals(4, (await identityService.GetAvailableLocationsAsync("eaa-admin")).Count, "Inactive location is excluded from sign-in options");
            AssertTrue(await identityService.SetLocationActiveAsync(administratorSession.SessionId, configuredLocationId, true), "Administrator can reactivate a location");
            bool selfDisableDenied = false;
            try { await identityService.SetUserActiveAsync(administratorSession.SessionId, administratorId, false); }
            catch (InvalidOperationException) { selfDisableDenied = true; }
            AssertTrue(selfDisableDenied, "An administrator cannot disable the active account in its own session");

            int instructorId = await identityService.CreateUserAsync(
                administratorSession.SessionId,
                "instructor.one",
                "Instructor One",
                "Instructor-Strong-Password-2026!",
                ["instructor"],
                ["OCT"]);
            AssertEquals(1, (await identityService.GetAvailableLocationsAsync("instructor.one")).Count, "User location assignment constrains available sign-in locations");
            AssertEquals<UserSession?>(null, await identityService.AuthenticateAsync("instructor.one", "Instructor-Strong-Password-2026!", "ASY"), "Login from an unassigned location is denied");
            using (var identityConnection = await identityDatabase.OpenIdentityConnectionAsync())
            {
                using var passwordCommand = identityConnection.CreateCommand();
                passwordCommand.CommandText = "SELECT PasswordHash FROM Users WHERE Id = @userId;";
                passwordCommand.Parameters.AddWithValue("@userId", instructorId);
                string passwordHash = Convert.ToString(await passwordCommand.ExecuteScalarAsync())!;
                AssertTrue(passwordHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal), "Password is stored as a salted PBKDF2 hash");
                AssertTrue(!passwordHash.Contains("Instructor-Strong-Password-2026!", StringComparison.Ordinal), "Plaintext password is not stored");
            }

            var instructorSession = await identityService.AuthenticateAsync("instructor.one", "Instructor-Strong-Password-2026!", "OCT");
            AssertTrue(instructorSession != null, "Active instructor can authenticate");
            AssertTrue(await identityService.HasPermissionAsync(instructorSession!.SessionId, "flight-records", PermissionLevel.FullEdit), "Instructor receives configured flight-record permissions");
            AssertTrue(!await identityService.HasPermissionAsync(instructorSession.SessionId, "finance", PermissionLevel.FullEdit), "Instructor is denied finance permissions");

            bool directDatabaseWriteDenied = false;
            try
            {
                await identityDatabase.SaveAircraftResourceAsync(new AircraftResource { Registration = "SU-DENY", Status = "Available" });
            }
            catch (UnauthorizedAccessException)
            {
                directDatabaseWriteDenied = true;
            }
            AssertTrue(directDatabaseWriteDenied, "Database service enforces the permission matrix on direct mutation calls");
            AssertTrue(!(await identityDatabase.GetAircraftResourcesAsync()).Exists(r => r.Registration == "SU-DENY"), "Denied direct mutation does not persist data");

            bool directDenial = false;
            try
            {
                await identityService.RequirePermissionAsync(instructorSession.SessionId, "finance", PermissionLevel.FullEdit);
            }
            catch (UnauthorizedAccessException)
            {
                directDenial = true;
            }
            AssertTrue(directDenial, "Service-level authorization rejects a direct unauthorized call");

            bool directReadDenial = false;
            try
            {
                await identityDatabase.GetAuditEventsAsync(limit: 1);
            }
            catch (UnauthorizedAccessException)
            {
                directReadDenial = true;
            }
            AssertTrue(directReadDenial, "Database service enforces read permissions on direct retrieval calls");

            AssertTrue(await identityService.SetUserActiveAsync(administratorSession.SessionId, instructorId, false), "Administrator can disable an account");
            AssertEquals<UserSession?>(null, await identityService.GetActiveSessionAsync(instructorSession.SessionId), "Disabling a user revokes active sessions");
            AssertEquals<UserSession?>(null, await identityService.AuthenticateAsync("instructor.one", "Instructor-Strong-Password-2026!"), "Disabled user cannot log in");
            AssertTrue((await identityService.GetUsersAsync(administratorSession.SessionId)).Exists(u => u.Id == instructorId && !u.IsActive), "Disabled account remains available for historical attribution");

            var administratorAuditSession = await identityService.AuthenticateAsync("eaa-admin", "Initial-Admin-Password-2026!", "OCT");
            AssertTrue(administratorAuditSession != null, "Administrator can sign back in to review audit history");

            int approvalStudentId = await identityDatabase.GetOrCreateStudentAsync("Approval Workflow Student");
            int approvalOrderId = await identityDatabase.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = approvalStudentId,
                OrderNumber = "APPROVAL-001",
                ProgramType = "PPL",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2026, 1, 1),
                Year = 2026,
                AcademicYear = 2026,
                RegulatoryTrack = "Part61",
                SequenceNumber = 1
            });

            bool badApprovalCredentialDenied = false;
            try
            {
                await identityService.RecordApprovalAsync(administratorAuditSession!.SessionId, "TrainingOrder", approvalOrderId, "Complete", "Approved graduation evidence", "incorrect-password");
            }
            catch (UnauthorizedAccessException)
            {
                badApprovalCredentialDenied = true;
            }
            AssertTrue(badApprovalCredentialDenied, "Approval requires password re-verification");
            await identityService.RecordApprovalAsync(administratorAuditSession!.SessionId, "TrainingOrder", approvalOrderId, "Complete", "Approved completion evidence", "Initial-Admin-Password-2026!");
            AssertTrue(await identityDatabase.CompleteOrderAsync(approvalOrderId, new DateTime(2026, 6, 1), "Approved by training authority"), "Approved completion transition succeeds");

            bool duplicateApprovalUseDenied = false;
            try
            {
                await identityDatabase.CompleteOrderAsync(approvalOrderId, new DateTime(2026, 6, 1));
            }
            catch (UnauthorizedAccessException)
            {
                duplicateApprovalUseDenied = true;
            }
            AssertTrue(duplicateApprovalUseDenied, "An approval can be consumed only once");
            var orderAudit = await identityDatabase.GetAuditEventsAsync("TrainingOrder", approvalOrderId);
            var completionAudit = orderAudit.Find(e => e.Action == "Completed");
            AssertTrue(completionAudit?.VersionNo == 2 && completionAudit.BeforeJson != null && completionAudit.AfterJson != null, "Sensitive training completion stores immutable before/after version evidence");

            var userAudit = await identityDatabase.GetAuditEventsAsync("User", instructorId);
            var userCreatedAudit = userAudit.Find(e => e.Action == "Created");
            AssertTrue(userCreatedAudit != null && userCreatedAudit.Actor == administratorSession.UserName, "User creation is attributable to the acting administrator");
            AssertEquals(administratorId, userCreatedAudit!.UserId, "Identity audit includes the acting user ID");
            AssertEquals(administratorSession.SessionId, userCreatedAudit.SessionId, "Identity audit includes the acting session ID");
            AssertEquals(administratorSession.LocationId, userCreatedAudit.LocationId, "Identity audit includes the operating location");
            AssertTrue(userAudit.Exists(e => e.Action == "Disabled" && e.Actor == administratorSession.UserName), "Account disable is auditable without deleting the user");

            int auditedResourceId = await identityDatabase.SaveAircraftResourceAsync(new AircraftResource { Registration = "SU-AUDIT", Status = "Available" });
            await identityDatabase.SaveAircraftResourceAsync(new AircraftResource { Registration = "SU-AUDIT", Status = "Available", HobbsHours = 12.5 });
            var resourceAudit = await identityDatabase.GetAuditEventsAsync("AircraftResource", auditedResourceId);
            var updatedResourceAudit = resourceAudit.Find(e => e.VersionNo == 2);
            AssertTrue(resourceAudit.Exists(e => e.Actor == administratorAuditSession!.UserName && e.UserId == administratorId && e.SessionId == administratorAuditSession.SessionId && e.LocationId == administratorAuditSession.LocationId), "Operational audit records user, session, and location context");
            AssertTrue(updatedResourceAudit?.BeforeJson?.Contains("HobbsHours", StringComparison.Ordinal) == true && updatedResourceAudit.AfterJson?.Contains("12.5", StringComparison.Ordinal) == true, "Audit history stores before/after snapshots and entity version numbers");

            bool auditUpdateRejected = false;
            bool auditDeleteRejected = false;
            bool userDeleteRejected = false;
            using (var auditConnection = await identityDatabase.OpenIdentityConnectionAsync())
            {
                using var updateAudit = auditConnection.CreateCommand();
                updateAudit.CommandText = "UPDATE AuditEvents SET Summary = 'tampered' WHERE EntityType = 'User' AND EntityId = @userId;";
                updateAudit.Parameters.AddWithValue("@userId", instructorId);
                try { await updateAudit.ExecuteNonQueryAsync(); }
                catch (SqliteException) { auditUpdateRejected = true; }

                using var deleteAudit = auditConnection.CreateCommand();
                deleteAudit.CommandText = "DELETE FROM AuditEvents WHERE EntityType = 'User' AND EntityId = @userId;";
                deleteAudit.Parameters.AddWithValue("@userId", instructorId);
                try { await deleteAudit.ExecuteNonQueryAsync(); }
                catch (SqliteException) { auditDeleteRejected = true; }

                using var deleteUser = auditConnection.CreateCommand();
                deleteUser.CommandText = "DELETE FROM Users WHERE Id = @userId;";
                deleteUser.Parameters.AddWithValue("@userId", instructorId);
                try { await deleteUser.ExecuteNonQueryAsync(); }
                catch (SqliteException) { userDeleteRejected = true; }
            }
            AssertTrue(auditUpdateRejected && auditDeleteRejected, "SQLite prevents application-level audit updates and deletes");
            AssertTrue(userDeleteRejected, "SQLite preserves disabled accounts for historical attribution");
            AssertTrue(await identityService.EndSessionAsync(administratorAuditSession!.SessionId), "Explicit logout ends the active session");
            AssertTrue(!await identityService.HasPermissionAsync(administratorAuditSession.SessionId, "users", PermissionLevel.ReadOnly), "Expired session no longer authorizes service calls");
            Console.WriteLine("  ✔ Identity, role grants, session revocation, attribution, and append-only audit verified!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 21 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 22: Versioned Curriculum, Regulatory Control & Electronic Training Records
        Console.WriteLine("\n[TEST SUITE 22] Testing Versioned Curriculum & Regulatory Training Records...");
        try
        {
            string curriculumDatabasePath = Path.Combine(_testDataDirectory, "curriculum", "eaa_training.db");
            var curriculumDatabase = new DatabaseService(curriculumDatabasePath);
            await curriculumDatabase.InitializeAsync();
            var curriculumIdentity = new IdentityService(curriculumDatabase);
            curriculumDatabase.AttachIdentityService(curriculumIdentity);
            await curriculumIdentity.EnsureAuthorizationCatalogAsync();
            var curriculumService = new CurriculumService(curriculumDatabase, curriculumIdentity);

            async Task<string> ReadReviewStatusAsync(string table, int id)
            {
                using var connection = await curriculumDatabase.OpenIdentityConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT ReviewStatus FROM {table} WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", id);
                string? status = Convert.ToString(await command.ExecuteScalarAsync());
                AssertTrue(status != null, $"Row {id} exists in {table}");
                return status!;
            }

            async Task<string> ReadObjectiveStampAsync(DatabaseService database, int progressId)
            {
                using var connection = await database.OpenIdentityConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT RegulatoryReviewStatus FROM ObjectiveProgress WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", progressId);
                string? status = Convert.ToString(await command.ExecuteScalarAsync());
                AssertTrue(status != null, $"Objective progress row {progressId} exists");
                return status!;
            }

            async Task<string> ReadObjectiveRevisionAsync(DatabaseService database, int progressId)
            {
                using var connection = await database.OpenIdentityConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COALESCE(RegulatoryRevision, '') FROM ObjectiveProgress WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", progressId);
                return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
            }

            async Task<int?> ReadObjectiveApprovalAsync(DatabaseService database, int progressId)
            {
                using var connection = await database.OpenIdentityConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT ApprovalRecordId FROM ObjectiveProgress WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", progressId);
                object? value = await command.ExecuteScalarAsync();
                return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
            }

            int curriculumAdminId = await curriculumIdentity.CreateBootstrapAdministratorAsync("eaa-admin", "EAA Administrator", "Initial-Admin-Password-2026!");
            var curriculumAdminSession = await curriculumIdentity.AuthenticateAsync("eaa-admin", "Initial-Admin-Password-2026!", "OCT");
            AssertTrue(curriculumAdminSession != null, "Administrator can open the curriculum verification session");
            string curriculumSessionId = curriculumAdminSession!.SessionId;

            await curriculumService.EnsureStreamCatalogAsync();
            var streamTemplates = await curriculumService.GetStreamTemplatesAsync();
            AssertEquals(5, streamTemplates.Count, "Five controlled training streams are registered");
            AssertTrue(streamTemplates.Exists(t => t.StreamKey == "Part61" && t.ArabicName == "نظام حر"), "Part 61 stream label is registered in Arabic");
            AssertTrue(streamTemplates.Exists(t => t.StreamKey == "Part141" && t.ArabicName == "نظام معتمد"), "Part 141 stream label is registered in Arabic");
            AssertTrue(streamTemplates.Exists(t => t.StreamKey == "ETP" && t.ArabicName == "خط جوي"), "ETP stream label is registered in Arabic");
            AssertTrue(streamTemplates.Exists(t => t.StreamKey == "TypeRating" && t.ArabicName == "تجديد طراز وفرق وبناء ساعات"), "Type rating stream label is registered in Arabic");
            AssertTrue(streamTemplates.Exists(t => t.StreamKey == "Evaluation" && t.ArabicName == "تقييم ومعادلات"), "Evaluation stream label is registered in Arabic");

            int frameworkId;
            using (var frameworkConnection = await curriculumDatabase.OpenIdentityConnectionAsync())
            {
                using var frameworkCommand = frameworkConnection.CreateCommand();
                frameworkCommand.CommandText = "SELECT Id FROM RegulatoryFrameworks WHERE FrameworkCode = 'ECAR-ECAA';";
                frameworkId = Convert.ToInt32(await frameworkCommand.ExecuteScalarAsync());
            }
            AssertTrue(frameworkId > 0, "Controlled regulatory framework family is seeded");

            int incompleteDocId = await curriculumService.RegisterRegulatoryDocumentAsync(curriculumSessionId, new RegulatoryDocument { FrameworkId = frameworkId });
            int approvedDocId = await curriculumService.RegisterRegulatoryDocumentAsync(curriculumSessionId, new RegulatoryDocument
            {
                FrameworkId = frameworkId,
                Part = "Part-61",
                Issue = "2026-01",
                Revision = "R1",
                EffectiveFrom = new DateTime(2026, 1, 1),
                SourceFile = "ECAR_Part61_2026.pdf",
                ApprovingAuthority = "ECAA"
            });
            AssertEquals("NeedsRegulatoryReview", await ReadReviewStatusAsync("RegulatoryDocuments", incompleteDocId), "Incomplete source metadata defaults to NeedsRegulatoryReview");
            AssertEquals("Draft", await ReadReviewStatusAsync("RegulatoryDocuments", approvedDocId), "Complete source metadata enters draft");

            bool incompleteReviewDenied = false;
            try { await curriculumService.SetRegulatoryDocumentStatusAsync(curriculumSessionId, incompleteDocId, ReviewStatus.UnderReview); }
            catch (InvalidOperationException) { incompleteReviewDenied = true; }
            AssertTrue(incompleteReviewDenied, "Incomplete source metadata cannot enter review");

            await curriculumService.SetRegulatoryDocumentStatusAsync(curriculumSessionId, approvedDocId, ReviewStatus.UnderReview);
            AssertEquals("UnderReview", await ReadReviewStatusAsync("RegulatoryDocuments", approvedDocId), "Complete controlled document can enter review");

            bool documentApprovalEvidenceDenied = false;
            try { await curriculumService.SetRegulatoryDocumentStatusAsync(curriculumSessionId, approvedDocId, ReviewStatus.Approved); }
            catch (UnauthorizedAccessException) { documentApprovalEvidenceDenied = true; }
            AssertTrue(documentApprovalEvidenceDenied, "Direct document approval without approval evidence is denied");

            bool documentBadPasswordDenied = false;
            try { await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "RegulatoryDocument", approvedDocId, "Approve", "Approved controlled source", "wrong-password"); }
            catch (UnauthorizedAccessException) { documentBadPasswordDenied = true; }
            AssertTrue(documentBadPasswordDenied, "Approval recording re-verifies the approver password");

            int mismatchedEvidenceId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "CurriculumVersion", 999, "Approve", "Evidence recorded for the wrong entity", "Initial-Admin-Password-2026!");
            bool documentMismatchedEvidenceDenied = false;
            try { await curriculumService.SetRegulatoryDocumentStatusAsync(curriculumSessionId, approvedDocId, ReviewStatus.Approved, mismatchedEvidenceId); }
            catch (UnauthorizedAccessException) { documentMismatchedEvidenceDenied = true; }
            AssertTrue(documentMismatchedEvidenceDenied, "Approval evidence recorded for a different entity cannot approve this document");

            int documentApprovalRecordId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "RegulatoryDocument", approvedDocId, "Approve", "Approved controlled training source", "Initial-Admin-Password-2026!");
            await curriculumService.SetRegulatoryDocumentStatusAsync(curriculumSessionId, approvedDocId, ReviewStatus.Approved, documentApprovalRecordId);
            AssertEquals("Approved", await ReadReviewStatusAsync("RegulatoryDocuments", approvedDocId), "Verified approval evidence approves the controlled document");

            int part61TemplateId = streamTemplates.Find(t => t.StreamKey == "Part61")!.Id;
            DateOnly curriculumEffectiveFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            int unlinkedVersionId = await curriculumService.CreateCurriculumVersionAsync(curriculumSessionId, part61TemplateId, "V-NO-DOC", curriculumEffectiveFrom, null, null, "Version without a controlled source");
            int unapprovedSourceVersionId = await curriculumService.CreateCurriculumVersionAsync(curriculumSessionId, part61TemplateId, "V-INCOMPLETE-DOC", curriculumEffectiveFrom, null, incompleteDocId, "Version bound to an incomplete source");
            int teachingVersionId = await curriculumService.CreateCurriculumVersionAsync(curriculumSessionId, part61TemplateId, "V-2026.1", curriculumEffectiveFrom, null, approvedDocId, "Approved Part 61 syllabus");
            AssertEquals("NeedsRegulatoryReview", await ReadReviewStatusAsync("CurriculumVersions", unlinkedVersionId), "Version without a controlled source requires regulatory review");
            AssertEquals("NeedsRegulatoryReview", await ReadReviewStatusAsync("CurriculumVersions", unapprovedSourceVersionId), "Version bound to an unapproved source requires regulatory review");
            AssertEquals("Draft", await ReadReviewStatusAsync("CurriculumVersions", teachingVersionId), "Version bound to an approved complete source enters draft");

            bool streamMismatchDenied = false;
            try
            {
                await curriculumService.CreateProgramApprovalAsync(curriculumSessionId, new ProgramApproval
                {
                    StreamKey = "Part141",
                    CurriculumVersionId = teachingVersionId,
                    RegulatoryDocumentId = approvedDocId,
                    TrainingMethod = "Dual",
                    AircraftOrSimulatorScope = "C172",
                    ValidFrom = DateTime.Today.AddDays(-1)
                });
            }
            catch (InvalidOperationException) { streamMismatchDenied = true; }
            AssertTrue(streamMismatchDenied, "Program approval cannot bind a version from a different stream");

            int stuckApprovalId = await curriculumService.CreateProgramApprovalAsync(curriculumSessionId, new ProgramApproval
            {
                StreamKey = "Part61",
                CurriculumVersionId = unapprovedSourceVersionId,
                RegulatoryDocumentId = incompleteDocId,
                TrainingMethod = "Dual",
                AircraftOrSimulatorScope = "C172",
                ValidFrom = DateTime.Today.AddDays(-1)
            });
            AssertEquals("NeedsRegulatoryReview", await ReadReviewStatusAsync("ProgramApprovals", stuckApprovalId), "Program approval against an unapproved source stays in regulatory review");
            bool stuckApprovalRejected = false;
            try { await curriculumService.ApproveProgramApprovalAsync(curriculumSessionId, stuckApprovalId, documentApprovalRecordId); }
            catch (InvalidOperationException) { stuckApprovalRejected = true; }
            AssertTrue(stuckApprovalRejected, "Program approval cannot be accepted while its source document is unapproved");

            bool unapprovedPublishDenied = false;
            try { await curriculumService.PublishCurriculumVersionAsync(curriculumSessionId, unapprovedSourceVersionId, documentApprovalRecordId); }
            catch (InvalidOperationException) { unapprovedPublishDenied = true; }
            AssertTrue(unapprovedPublishDenied, "Publishing against an unapproved source document is rejected");

            int teachingApprovalId = await curriculumService.CreateProgramApprovalAsync(curriculumSessionId, new ProgramApproval
            {
                StreamKey = "Part61",
                CurriculumVersionId = teachingVersionId,
                RegulatoryDocumentId = approvedDocId,
                TrainingMethod = "Dual",
                AircraftOrSimulatorScope = "C172",
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidTo = DateTime.Today.AddYears(1),
                Limitations = "Student solo subject to school policy"
            });
            AssertEquals("UnderReview", await ReadReviewStatusAsync("ProgramApprovals", teachingApprovalId), "Approved source allows the program approval to enter review");

            bool teachingApprovalEvidenceDenied = false;
            try { await curriculumService.ApproveProgramApprovalAsync(curriculumSessionId, teachingApprovalId, 99999); }
            catch (UnauthorizedAccessException) { teachingApprovalEvidenceDenied = true; }
            AssertTrue(teachingApprovalEvidenceDenied, "Program approval acceptance requires recorded approval evidence");

            int teachingApprovalRecordId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "ProgramApproval", teachingApprovalId, "Approve", "Program scope approved for delivery", "Initial-Admin-Password-2026!");
            bool teachingApprovalMismatchDenied = false;
            try { await curriculumService.ApproveProgramApprovalAsync(curriculumSessionId, teachingApprovalId, teachingApprovalRecordId + 4242); }
            catch (UnauthorizedAccessException) { teachingApprovalMismatchDenied = true; }
            AssertTrue(teachingApprovalMismatchDenied, "Mismatched approval evidence is rejected for program approval");
            await curriculumService.ApproveProgramApprovalAsync(curriculumSessionId, teachingApprovalId, teachingApprovalRecordId);
            AssertEquals("Approved", await ReadReviewStatusAsync("ProgramApprovals", teachingApprovalId), "Program approval accepted with verified evidence");

            bool publishEvidenceDenied = false;
            try { await curriculumService.PublishCurriculumVersionAsync(curriculumSessionId, teachingVersionId, 99999); }
            catch (UnauthorizedAccessException) { publishEvidenceDenied = true; }
            AssertTrue(publishEvidenceDenied, "Publication requires recorded approval evidence");

            int publishRecordId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "CurriculumVersion", teachingVersionId, "Publish", "Syllabus approved for student use", "Initial-Admin-Password-2026!");
            bool publishMismatchDenied = false;
            try { await curriculumService.PublishCurriculumVersionAsync(curriculumSessionId, teachingVersionId, publishRecordId + 7); }
            catch (UnauthorizedAccessException) { publishMismatchDenied = true; }
            AssertTrue(publishMismatchDenied, "Mismatched approval evidence is rejected for publication");
            await curriculumService.PublishCurriculumVersionAsync(curriculumSessionId, teachingVersionId, publishRecordId);
            AssertEquals("Approved", await ReadReviewStatusAsync("CurriculumVersions", teachingVersionId), "Curriculum version publishes against approved source and program approval");

            bool publishedImmutable = false;
            try
            {
                await curriculumService.AddLessonAsync(curriculumSessionId, new CurriculumLesson
                {
                    CurriculumVersionId = teachingVersionId,
                    StageCode = "STAGE-1",
                    LessonCode = "L-NEW",
                    ArabicTitle = "درس جديد",
                    EnglishTitle = "New Lesson",
                    SequenceNumber = 99
                });
            }
            catch (InvalidOperationException) { publishedImmutable = true; }
            AssertTrue(publishedImmutable, "Published curriculum versions are immutable");

            int workingVersionId = await curriculumService.CreateCurriculumVersionAsync(curriculumSessionId, part61TemplateId, "V-WORK", curriculumEffectiveFrom, null, approvedDocId, "Editable working copy");
            int lessonOneId = await curriculumService.AddLessonAsync(curriculumSessionId, new CurriculumLesson
            {
                CurriculumVersionId = workingVersionId, StageCode = "STAGE-1", LessonCode = "L-101",
                ArabicTitle = "أساسيات الطيران", EnglishTitle = "Flight Basics", SequenceNumber = 1
            });
            int lessonTwoId = await curriculumService.AddLessonAsync(curriculumSessionId, new CurriculumLesson
            {
                CurriculumVersionId = workingVersionId, StageCode = "STAGE-1", LessonCode = "L-102",
                ArabicTitle = "المناورات", EnglishTitle = "Maneuvering", SequenceNumber = 2
            });
            int objectiveOneId = await curriculumService.AddObjectiveAsync(curriculumSessionId, new TrainingObjective
            {
                CurriculumLessonId = lessonOneId, ObjectiveCode = "OBJ-1",
                ArabicDescription = "أداء الطيران المستقيم والمستوي", EnglishDescription = "Perform straight and level flight",
                CompletionStandard = "Consistent execution within approved tolerances", EvidenceType = "Instructor demonstration"
            });
            int objectiveTwoId = await curriculumService.AddObjectiveAsync(curriculumSessionId, new TrainingObjective
            {
                CurriculumLessonId = lessonTwoId, ObjectiveCode = "OBJ-2",
                ArabicDescription = "أداء مناورات أساسية", EnglishDescription = "Perform basic maneuvers",
                CompletionStandard = "All maneuvers demonstrated to standard", EvidenceType = "Instructor evaluation"
            });
            AssertTrue(lessonOneId > 0 && objectiveTwoId > 0, "Lessons and objectives are created under the working version");

            int otherVersionId = await curriculumService.CreateCurriculumVersionAsync(curriculumSessionId, part61TemplateId, "V-OTHER", curriculumEffectiveFrom, null, approvedDocId, "Separate version");
            int otherLessonId = await curriculumService.AddLessonAsync(curriculumSessionId, new CurriculumLesson
            {
                CurriculumVersionId = otherVersionId, StageCode = "STAGE-1", LessonCode = "L-900",
                ArabicTitle = "درس منفصل", EnglishTitle = "Separate Lesson", SequenceNumber = 1
            });
            int otherObjectiveId = await curriculumService.AddObjectiveAsync(curriculumSessionId, new TrainingObjective
            {
                CurriculumLessonId = otherLessonId, ObjectiveCode = "OBJ-9",
                ArabicDescription = "هدف منفصل", EnglishDescription = "Separate objective",
                CompletionStandard = "Separate standard", EvidenceType = "Instructor evaluation"
            });

            bool crossVersionPrerequisiteDenied = false;
            try { await curriculumService.AddPrerequisiteAsync(curriculumSessionId, objectiveOneId, otherObjectiveId); }
            catch (InvalidOperationException) { crossVersionPrerequisiteDenied = true; }
            AssertTrue(crossVersionPrerequisiteDenied, "Prerequisites must reference objectives in the same version");

            bool selfPrerequisiteDenied = false;
            try { await curriculumService.AddPrerequisiteAsync(curriculumSessionId, objectiveOneId, objectiveOneId); }
            catch (InvalidOperationException) { selfPrerequisiteDenied = true; }
            AssertTrue(selfPrerequisiteDenied, "An objective cannot require itself");

            await curriculumService.AddPrerequisiteAsync(curriculumSessionId, objectiveTwoId, objectiveOneId);
            bool cyclePrerequisiteDenied = false;
            try { await curriculumService.AddPrerequisiteAsync(curriculumSessionId, objectiveOneId, objectiveTwoId); }
            catch (InvalidOperationException) { cyclePrerequisiteDenied = true; }
            AssertTrue(cyclePrerequisiteDenied, "Circular prerequisites are rejected");

            int curriculumStudentId = await curriculumDatabase.GetOrCreateStudentAsync("Curriculum E2E Student");
            int curriculumOrderId = await curriculumDatabase.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = curriculumStudentId,
                OrderNumber = "CURR-001",
                ProgramType = "PPL",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2026, 1, 1),
                Year = 2026,
                AcademicYear = 2026,
                RegulatoryTrack = "Part61",
                SequenceNumber = 1
            });

            bool prerequisiteBlocked = false;
            try { await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveTwoId, "Satisfactory", "Attempt before prerequisite completion"); }
            catch (InvalidOperationException) { prerequisiteBlocked = true; }
            AssertTrue(prerequisiteBlocked, "Objectives cannot be graded before prerequisites are complete");

            int firstAttemptId = await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveOneId, "Satisfactory", "Pre-publication grading");
            AssertEquals("NeedsRegulatoryReview", await ReadObjectiveStampAsync(curriculumDatabase, firstAttemptId), "Pre-publication grading is stamped NeedsRegulatoryReview");
            await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveTwoId, "Unsatisfactory", "Failed objective requires remediation");

            int workingApprovalId = await curriculumService.CreateProgramApprovalAsync(curriculumSessionId, new ProgramApproval
            {
                StreamKey = "Part61",
                CurriculumVersionId = workingVersionId,
                RegulatoryDocumentId = approvedDocId,
                TrainingMethod = "Dual",
                AircraftOrSimulatorScope = "C172",
                ValidFrom = DateTime.Today.AddDays(-1)
            });
            int workingApprovalRecordId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "ProgramApproval", workingApprovalId, "Approve", "Working syllabus scope approved", "Initial-Admin-Password-2026!");
            await curriculumService.ApproveProgramApprovalAsync(curriculumSessionId, workingApprovalId, workingApprovalRecordId);
            int workingPublishRecordId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "CurriculumVersion", workingVersionId, "Publish", "Working syllabus released for delivery", "Initial-Admin-Password-2026!");
            await curriculumService.PublishCurriculumVersionAsync(curriculumSessionId, workingVersionId, workingPublishRecordId);
            AssertEquals("Approved", await ReadReviewStatusAsync("CurriculumVersions", workingVersionId), "Working syllabus publishes with verified evidence");
            AssertEquals("Superseded", await ReadReviewStatusAsync("CurriculumVersions", teachingVersionId), "Publishing a newer syllabus supersedes the prior version without altering it");

            int secondAttemptId = await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveOneId, "Satisfactory", "Post-publication grading");
            AssertEquals("Approved", await ReadObjectiveStampAsync(curriculumDatabase, secondAttemptId), "Post-publication grading carries the Approved regulatory stamp");
            AssertEquals("R1", await ReadObjectiveRevisionAsync(curriculumDatabase, secondAttemptId), "Graded evidence captures the source document revision");

            bool waiverEvidenceDenied = false;
            try { await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveOneId, "Waived", "Waiver without evidence"); }
            catch (UnauthorizedAccessException) { waiverEvidenceDenied = true; }
            AssertTrue(waiverEvidenceDenied, "Waiver grading requires recorded approval evidence");

            int waiverRecordId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "TrainingObjective", objectiveOneId, "Waive", "Authorized equivalence waiver", "Initial-Admin-Password-2026!");
            bool waiverMismatchDenied = false;
            try { await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveOneId, "Waived", "Mismatched waiver evidence", null, waiverRecordId + 5); }
            catch (UnauthorizedAccessException) { waiverMismatchDenied = true; }
            AssertTrue(waiverMismatchDenied, "Mismatched approval evidence is rejected for waivers");

            int waiverProgressId = await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveOneId, "Waived", "Approved equivalence waiver", null, waiverRecordId);
            AssertEquals(waiverRecordId, await ReadObjectiveApprovalAsync(curriculumDatabase, waiverProgressId), "Waiver rows carry their approval evidence reference");

            int supersedeRecordId = await curriculumIdentity.RecordApprovalAsync(curriculumSessionId, "RegulatoryDocument", approvedDocId, "Supersede", "Revision superseded by authority", "Initial-Admin-Password-2026!");
            await curriculumService.SetRegulatoryDocumentStatusAsync(curriculumSessionId, approvedDocId, ReviewStatus.Superseded, supersedeRecordId);
            AssertEquals("Superseded", await ReadReviewStatusAsync("RegulatoryDocuments", approvedDocId), "Approved controlled document can be superseded with approval evidence");

            int postSupersedeAttemptId = await curriculumService.RecordObjectiveProgressAsync(curriculumSessionId, curriculumStudentId, curriculumOrderId, objectiveOneId, "Satisfactory", "Grading under a superseded source");
            AssertEquals("NeedsRegulatoryReview", await ReadObjectiveStampAsync(curriculumDatabase, postSupersedeAttemptId), "Grading after source supersession is stamped NeedsRegulatoryReview");
            AssertEquals("Approved", await ReadObjectiveStampAsync(curriculumDatabase, secondAttemptId), "Historical grading rows retain their original regulatory stamp");

            bool progressUpdateRejected = false;
            bool progressDeleteRejected = false;
            using (var progressConnection = await curriculumDatabase.OpenIdentityConnectionAsync())
            {
                using var updateProgress = progressConnection.CreateCommand();
                updateProgress.CommandText = "UPDATE ObjectiveProgress SET Result = 'Satisfactory' WHERE Id = @id;";
                updateProgress.Parameters.AddWithValue("@id", firstAttemptId);
                try { await updateProgress.ExecuteNonQueryAsync(); }
                catch (SqliteException) { progressUpdateRejected = true; }

                using var deleteProgress = progressConnection.CreateCommand();
                deleteProgress.CommandText = "DELETE FROM ObjectiveProgress WHERE Id = @id;";
                deleteProgress.Parameters.AddWithValue("@id", firstAttemptId);
                try { await deleteProgress.ExecuteNonQueryAsync(); }
                catch (SqliteException) { progressDeleteRejected = true; }
            }
            AssertTrue(progressUpdateRejected && progressDeleteRejected, "Objective progress evidence is append-only");

            var trajectory = await curriculumService.GetObjectiveProgressAsync(curriculumStudentId, curriculumOrderId);
            AssertEquals(5, trajectory.Count, "Complete objective trajectory is reconstructable");
            AssertTrue(trajectory.TrueForAll(p => p.GraderUserId == curriculumAdminId), "Every graded row is attributable to the grading user");
            AssertTrue(trajectory.Exists(p => p.ObjectiveId == objectiveTwoId && p.Result == "Unsatisfactory"), "Failed objectives remain visible in the trajectory");
            AssertTrue(trajectory.Exists(p => p.Result == "Waived" && p.ApprovalRecordId == waiverRecordId), "Waiver evidence is reconstructable from the trajectory");
            AssertTrue(trajectory.Exists(p => p.CurriculumVersionId == workingVersionId), "Trajectory rows reference the governing curriculum version");
            Console.WriteLine("  ✔ Versioned curriculum, approval-gated publication, and append-only training records verified!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 22 FAILED: {ex.Message}");
            failures++;
        }

        // TEST SUITE 23: Full Curriculum End-to-End — Session to Graduation
        Console.WriteLine("\n[TEST SUITE 23] Testing Full Curriculum End-to-End (Session → Remediation → Stage Check → Graduation)...");
        try
        {
            string e2eDatabasePath = Path.Combine(_testDataDirectory, "curriculum-e2e", "eaa_training.db");
            var e2eDatabase = new DatabaseService(e2eDatabasePath);
            await e2eDatabase.InitializeAsync();
            var e2eIdentity = new IdentityService(e2eDatabase);
            e2eDatabase.AttachIdentityService(e2eIdentity);
            await e2eIdentity.EnsureAuthorizationCatalogAsync();
            var e2eCurriculum = new CurriculumService(e2eDatabase, e2eIdentity);
            var e2eTrainingRecord = new TrainingRecordService(e2eDatabase, e2eIdentity);

            async Task<string> ReadE2EReviewStatusAsync(string table, int id)
            {
                using var connection = await e2eDatabase.OpenIdentityConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT ReviewStatus FROM {table} WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", id);
                string? status = Convert.ToString(await command.ExecuteScalarAsync());
                AssertTrue(status != null, $"Row {id} exists in {table}");
                return status!;
            }

            const string e2eAdminPassword = "Initial-Admin-Password-2026!";
            int e2eAdminId = await e2eIdentity.CreateBootstrapAdministratorAsync("eaa-admin", "EAA Administrator", e2eAdminPassword);
            var e2eAdminSession = await e2eIdentity.AuthenticateAsync("eaa-admin", e2eAdminPassword, "OCT");
            AssertTrue(e2eAdminSession != null, "Administrator can open the end-to-end verification session");
            string e2eSessionId = e2eAdminSession!.SessionId;

            await e2eCurriculum.EnsureStreamCatalogAsync();
            var e2eTemplates = await e2eCurriculum.GetStreamTemplatesAsync();
            int e2ePart61TemplateId = e2eTemplates.Find(t => t.StreamKey == "Part61")!.Id;

            int e2eFrameworkId;
            using (var e2eFrameworkConnection = await e2eDatabase.OpenIdentityConnectionAsync())
            {
                using var e2eFrameworkCommand = e2eFrameworkConnection.CreateCommand();
                e2eFrameworkCommand.CommandText = "SELECT Id FROM RegulatoryFrameworks WHERE FrameworkCode = 'ECAR-ECAA';";
                e2eFrameworkId = Convert.ToInt32(await e2eFrameworkCommand.ExecuteScalarAsync());
            }
            AssertTrue(e2eFrameworkId > 0, "Controlled regulatory framework family is seeded");

            int e2eDocumentId = await e2eCurriculum.RegisterRegulatoryDocumentAsync(e2eSessionId, new RegulatoryDocument
            {
                FrameworkId = e2eFrameworkId,
                Part = "Part-61",
                Issue = "2026-02",
                Revision = "R2",
                EffectiveFrom = new DateTime(2026, 2, 1),
                SourceFile = "ECAR_Part61_2026_02.pdf",
                ApprovingAuthority = "ECAA"
            });
            await e2eCurriculum.SetRegulatoryDocumentStatusAsync(e2eSessionId, e2eDocumentId, ReviewStatus.UnderReview);
            int e2eDocumentApprovalId = await e2eIdentity.RecordApprovalAsync(e2eSessionId, "RegulatoryDocument", e2eDocumentId, "Approve", "Approved controlled source for end-to-end delivery", e2eAdminPassword);
            await e2eCurriculum.SetRegulatoryDocumentStatusAsync(e2eSessionId, e2eDocumentId, ReviewStatus.Approved, e2eDocumentApprovalId);
            AssertEquals("Approved", await ReadE2EReviewStatusAsync("RegulatoryDocuments", e2eDocumentId), "Controlled source document is approved with verified evidence");

            DateOnly e2eEffectiveFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-60));
            int e2eVersionId = await e2eCurriculum.CreateCurriculumVersionAsync(e2eSessionId, e2ePart61TemplateId, "V-E2E-2026", e2eEffectiveFrom, null, e2eDocumentId, "End-to-end verified syllabus");
            int e2eLessonOneId = await e2eCurriculum.AddLessonAsync(e2eSessionId, new CurriculumLesson
            {
                CurriculumVersionId = e2eVersionId, StageCode = "STAGE-1", LessonCode = "L-101",
                ArabicTitle = "أساسيات الطيران", EnglishTitle = "Flight Basics", SequenceNumber = 1
            });
            int e2eLessonTwoId = await e2eCurriculum.AddLessonAsync(e2eSessionId, new CurriculumLesson
            {
                CurriculumVersionId = e2eVersionId, StageCode = "STAGE-1", LessonCode = "L-102",
                ArabicTitle = "المناورات", EnglishTitle = "Maneuvering", SequenceNumber = 2
            });
            int e2eObjectiveOneId = await e2eCurriculum.AddObjectiveAsync(e2eSessionId, new TrainingObjective
            {
                CurriculumLessonId = e2eLessonOneId, ObjectiveCode = "OBJ-E1",
                ArabicDescription = "أداء الطيران المستقيم والمستوي", EnglishDescription = "Perform straight and level flight",
                CompletionStandard = "Consistent execution within approved tolerances", EvidenceType = "Instructor demonstration"
            });
            int e2eObjectiveTwoId = await e2eCurriculum.AddObjectiveAsync(e2eSessionId, new TrainingObjective
            {
                CurriculumLessonId = e2eLessonTwoId, ObjectiveCode = "OBJ-E2",
                ArabicDescription = "أداء مناورات أساسية", EnglishDescription = "Perform basic maneuvers",
                CompletionStandard = "All maneuvers demonstrated to standard", EvidenceType = "Instructor evaluation"
            });
            await e2eCurriculum.AddPrerequisiteAsync(e2eSessionId, e2eObjectiveTwoId, e2eObjectiveOneId);

            int e2eProgramApprovalId = await e2eCurriculum.CreateProgramApprovalAsync(e2eSessionId, new ProgramApproval
            {
                StreamKey = "Part61",
                CurriculumVersionId = e2eVersionId,
                RegulatoryDocumentId = e2eDocumentId,
                TrainingMethod = "Dual",
                AircraftOrSimulatorScope = "C172, SU-E2E",
                ValidFrom = DateTime.Today.AddDays(-1),
                ValidTo = DateTime.Today.AddYears(1)
            });
            int e2eProgramApprovalRecordId = await e2eIdentity.RecordApprovalAsync(e2eSessionId, "ProgramApproval", e2eProgramApprovalId, "Approve", "Program scope approved for end-to-end delivery", e2eAdminPassword);
            await e2eCurriculum.ApproveProgramApprovalAsync(e2eSessionId, e2eProgramApprovalId, e2eProgramApprovalRecordId);
            int e2ePublishRecordId = await e2eIdentity.RecordApprovalAsync(e2eSessionId, "CurriculumVersion", e2eVersionId, "Publish", "Syllabus released for end-to-end delivery", e2eAdminPassword);
            await e2eCurriculum.PublishCurriculumVersionAsync(e2eSessionId, e2eVersionId, e2ePublishRecordId);
            AssertEquals("Approved", await ReadE2EReviewStatusAsync("CurriculumVersions", e2eVersionId), "End-to-end curriculum version publishes with verified evidence");

            int e2eStudentId = await e2eDatabase.GetOrCreateStudentAsync("E2E Curriculum Trainee");
            int e2eOtherStudentId = await e2eDatabase.GetOrCreateStudentAsync("E2E Second Trainee");
            int e2eOrderId = await e2eDatabase.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = e2eStudentId,
                OrderNumber = "E2E-001",
                ProgramType = "PPL",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2026, 1, 1),
                Year = 2026,
                AcademicYear = 2026,
                RegulatoryTrack = "Part61",
                SequenceNumber = 1
            });
            int e2eOtherOrderId = await e2eDatabase.InsertOrUpdateOrderAsync(new TrainingOrder
            {
                StudentId = e2eOtherStudentId,
                OrderNumber = "E2E-002",
                ProgramType = "PPL",
                RegulationCategory = "61 (نظام حر)",
                EnrollmentDate = new DateTime(2026, 1, 1),
                Year = 2026,
                AcademicYear = 2026,
                RegulatoryTrack = "Part61",
                SequenceNumber = 1
            });
            await e2eDatabase.SaveAircraftResourceAsync(new AircraftResource
            {
                Registration = "SU-E2E",
                ResourceType = "Aircraft",
                AircraftType = "C172",
                Status = "Available",
                HobbsHours = 10
            });
            int e2eScheduledSessionId = await e2eDatabase.ScheduleTrainingSessionAsync(new TrainingSession
            {
                StudentId = e2eStudentId,
                RegulatoryTrack = "Part61",
                LessonTitle = "E2E Flight Lesson",
                InstructorName = "CFI E2E",
                ResourceName = "SU-E2E",
                Location = "October Airport",
                StartAt = new DateTime(2026, 1, 10, 8, 0, 0),
                EndAt = new DateTime(2026, 1, 10, 10, 0, 0),
                Notes = "End-to-end verification flight"
            });
            AssertTrue(e2eScheduledSessionId > 0, "Lesson session is scheduled for the trainee");

            // Session objective linking
            bool e2eLinkUnknownSessionDenied = false;
            try { await e2eTrainingRecord.LinkSessionObjectivesAsync(e2eSessionId, 999999, [e2eObjectiveOneId]); }
            catch (InvalidOperationException) { e2eLinkUnknownSessionDenied = true; }
            AssertTrue(e2eLinkUnknownSessionDenied, "Linking objectives to a missing session is rejected");

            bool e2eLinkUnknownObjectiveDenied = false;
            try { await e2eTrainingRecord.LinkSessionObjectivesAsync(e2eSessionId, e2eScheduledSessionId, [999999]); }
            catch (InvalidOperationException) { e2eLinkUnknownObjectiveDenied = true; }
            AssertTrue(e2eLinkUnknownObjectiveDenied, "Linking a missing curriculum objective is rejected");

            bool e2eLinkEmptyDenied = false;
            try { await e2eTrainingRecord.LinkSessionObjectivesAsync(e2eSessionId, e2eScheduledSessionId, []); }
            catch (ArgumentException) { e2eLinkEmptyDenied = true; }
            AssertTrue(e2eLinkEmptyDenied, "Session links require at least one curriculum objective");

            AssertEquals(2, await e2eTrainingRecord.LinkSessionObjectivesAsync(e2eSessionId, e2eScheduledSessionId, [e2eObjectiveOneId, e2eObjectiveTwoId]), "Both curriculum objectives link to the scheduled lesson");
            AssertEquals(0, await e2eTrainingRecord.LinkSessionObjectivesAsync(e2eSessionId, e2eScheduledSessionId, [e2eObjectiveOneId]), "Re-linking an existing objective is idempotent");

            async Task<string> ReadE2EStampAsync(int progressId)
            {
                using var connection = await e2eDatabase.OpenIdentityConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT RegulatoryReviewStatus FROM ObjectiveProgress WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", progressId);
                string? status = Convert.ToString(await command.ExecuteScalarAsync());
                AssertTrue(status != null, $"Objective progress row {progressId} exists");
                return status!;
            }

            async Task<string> ReadE2ERevisionAsync(int progressId)
            {
                using var connection = await e2eDatabase.OpenIdentityConnectionAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COALESCE(RegulatoryRevision, '') FROM ObjectiveProgress WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", progressId);
                return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
            }

            // Grading: prerequisite enforcement
            bool e2ePrerequisiteBlocked = false;
            try { await e2eCurriculum.RecordObjectiveProgressAsync(e2eSessionId, e2eStudentId, e2eOrderId, e2eObjectiveTwoId, "Satisfactory", "Graded before prerequisite completion"); }
            catch (InvalidOperationException) { e2ePrerequisiteBlocked = true; }
            AssertTrue(e2ePrerequisiteBlocked, "Objectives cannot be graded before prerequisites are complete");

            int e2eFirstGradeId = await e2eCurriculum.RecordObjectiveProgressAsync(e2eSessionId, e2eStudentId, e2eOrderId, e2eObjectiveOneId, "Satisfactory", "Prerequisite objective demonstrated to standard", e2eScheduledSessionId);
            AssertEquals("Approved", await ReadE2EStampAsync(e2eFirstGradeId), "Post-publication grading carries the Approved regulatory stamp");
            AssertEquals("R2", await ReadE2ERevisionAsync(e2eFirstGradeId), "Graded evidence captures the source document revision R2");

            int e2eObjectiveTwoFailId = await e2eCurriculum.RecordObjectiveProgressAsync(e2eSessionId, e2eStudentId, e2eOrderId, e2eObjectiveTwoId, "Unsatisfactory", "Failed objective requires remediation", e2eScheduledSessionId);
            AssertTrue(e2eObjectiveTwoFailId > 0, "Failed objective attempt is recorded");

            // Remedial plan lifecycle
            bool e2eRemedialTextDenied = false;
            try { await e2eTrainingRecord.OpenRemedialPlanAsync(e2eSessionId, e2eObjectiveTwoFailId, "", "English plan only"); }
            catch (ArgumentException) { e2eRemedialTextDenied = true; }
            AssertTrue(e2eRemedialTextDenied, "Remedial plans require bilingual plan descriptions");

            bool e2eInactiveInstructorDenied = false;
            try { await e2eTrainingRecord.OpenRemedialPlanAsync(e2eSessionId, e2eObjectiveTwoFailId, "خطة معالجة", "Remedial plan", 999999); }
            catch (InvalidOperationException) { e2eInactiveInstructorDenied = true; }
            AssertTrue(e2eInactiveInstructorDenied, "Remedial plans reject missing or inactive instructor accounts");

            int e2eRemedialOneId = await e2eTrainingRecord.OpenRemedialPlanAsync(e2eSessionId, e2eObjectiveTwoFailId, "خطة معالجة تدريبية للمناورات", "Bilingual remedial training plan for maneuvering", null, new DateOnly(2026, 3, 1));
            AssertTrue(e2eRemedialOneId > 0, "Remedial plan opens from the failed objective attempt");
            var e2ePlans = await e2eTrainingRecord.GetRemedialPlansAsync(e2eStudentId, e2eOrderId);
            AssertEquals(1, e2ePlans.Count, "One remedial plan exists after the failed attempt");
            AssertEquals("Open", e2ePlans[0].Status, "New remedial plan starts in the Open state");
            AssertEquals("Approved", e2ePlans[0].RegulatoryReviewStatus.ToString(), "Remedial plan carries the governing regulatory stamp");

            bool e2eDuplicateRemedialDenied = false;
            try { await e2eTrainingRecord.OpenRemedialPlanAsync(e2eSessionId, e2eObjectiveTwoFailId, "خطة مكررة", "Duplicate plan"); }
            catch (InvalidOperationException) { e2eDuplicateRemedialDenied = true; }
            AssertTrue(e2eDuplicateRemedialDenied, "Only one active remedial plan can exist per objective");

            bool e2eInvalidRemedialTransitionDenied = false;
            try { await e2eTrainingRecord.UpdateRemedialPlanStatusAsync(e2eSessionId, e2eRemedialOneId, RemedialPlan.StatusWaived, "Waiver without approval evidence"); }
            catch (ArgumentException) { e2eInvalidRemedialTransitionDenied = true; }
            AssertTrue(e2eInvalidRemedialTransitionDenied, "Direct waivers outside the approval-gated path are rejected");

            bool e2eShortRemarksDenied = false;
            try { await e2eTrainingRecord.UpdateRemedialPlanStatusAsync(e2eSessionId, e2eRemedialOneId, RemedialPlan.StatusCompleted, "done"); }
            catch (ArgumentException) { e2eShortRemarksDenied = true; }
            AssertTrue(e2eShortRemarksDenied, "Completing a remedial plan requires substantive completion remarks");

            await e2eTrainingRecord.UpdateRemedialPlanStatusAsync(e2eSessionId, e2eRemedialOneId, RemedialPlan.StatusInProgress, "Remedial training underway");
            bool e2eInProgressDuplicateDenied = false;
            try { await e2eTrainingRecord.UpdateRemedialPlanStatusAsync(e2eSessionId, e2eRemedialOneId, RemedialPlan.StatusInProgress, "Still underway"); }
            catch (InvalidOperationException) { e2eInProgressDuplicateDenied = true; }
            AssertTrue(e2eInProgressDuplicateDenied, "An in-progress remedial plan cannot restart");

            await e2eTrainingRecord.UpdateRemedialPlanStatusAsync(e2eSessionId, e2eRemedialOneId, RemedialPlan.StatusCompleted, "Remediation completed to standard");
            bool e2eClosedRemedialImmutable = false;
            try { await e2eTrainingRecord.UpdateRemedialPlanStatusAsync(e2eSessionId, e2eRemedialOneId, RemedialPlan.StatusInProgress, "Attempting to reopen"); }
            catch (InvalidOperationException) { e2eClosedRemedialImmutable = true; }
            AssertTrue(e2eClosedRemedialImmutable, "Closed remedial plans are immutable");

            bool e2eWaiveClosedDenied = false;
            try { await e2eTrainingRecord.WaiveRemedialPlanAsync(e2eSessionId, e2eRemedialOneId, 1); }
            catch (InvalidOperationException) { e2eWaiveClosedDenied = true; }
            AssertTrue(e2eWaiveClosedDenied, "Completed remedial plans cannot be waived");

            // Re-grade after remediation
            int e2eObjectiveTwoPassId = await e2eCurriculum.RecordObjectiveProgressAsync(e2eSessionId, e2eStudentId, e2eOrderId, e2eObjectiveTwoId, "Satisfactory", "Remediation verified to standard", e2eScheduledSessionId);
            AssertEquals("Approved", await ReadE2EStampAsync(e2eObjectiveTwoPassId), "Remediation evidence is stamped Approved after publication");

            var e2eRequirements = await e2eTrainingRecord.GetRemainingRequirementsAsync(e2eStudentId, e2eOrderId);
            AssertEquals(2, e2eRequirements.Count, "Both curriculum objectives appear in the remaining-requirements view");
            var e2eRequirementOne = e2eRequirements.Find(r => r.ObjectiveId == e2eObjectiveOneId)!;
            var e2eRequirementTwo = e2eRequirements.Find(r => r.ObjectiveId == e2eObjectiveTwoId)!;
            AssertEquals("Completed", e2eRequirementOne.Status, "Prerequisite objective is complete");
            AssertEquals("Completed", e2eRequirementTwo.Status, "Remediated objective is complete");
            AssertEquals(2, e2eRequirementTwo.Attempts, "Attempt history is reconstructable from the requirements view");
            AssertEquals("Satisfactory", e2eRequirementTwo.LastResult, "Latest attempt result drives the displayed outcome");
            AssertTrue(!e2eRequirementOne.HasOpenRemedial && !e2eRequirementTwo.HasOpenRemedial, "No open remedial plans block the objectives");

            // Regression + approval-gated waiver on the first objective
            int e2eObjectiveOneRegressionId = await e2eCurriculum.RecordObjectiveProgressAsync(e2eSessionId, e2eStudentId, e2eOrderId, e2eObjectiveOneId, "Unsatisfactory", "Recheck found control issues", e2eScheduledSessionId);
            int e2eWaiverPlanId = await e2eTrainingRecord.OpenRemedialPlanAsync(e2eSessionId, e2eObjectiveOneRegressionId, "خطة معالجة بديلة معتمدة", "Authority-approved alternate remedial plan");
            int e2eWaiverApprovalId = await e2eIdentity.RecordApprovalAsync(e2eSessionId, "RemedialPlan", e2eWaiverPlanId, "Waive", "Alternate evidence accepted by training authority", e2eAdminPassword);
            bool e2eWaiverMismatchDenied = false;
            try { await e2eTrainingRecord.WaiveRemedialPlanAsync(e2eSessionId, e2eWaiverPlanId, e2eWaiverApprovalId + 7); }
            catch (UnauthorizedAccessException) { e2eWaiverMismatchDenied = true; }
            AssertTrue(e2eWaiverMismatchDenied, "Mismatched approval evidence is rejected for remedial waivers");
            await e2eTrainingRecord.WaiveRemedialPlanAsync(e2eSessionId, e2eWaiverPlanId, e2eWaiverApprovalId);
            bool e2eDoubleWaiveDenied = false;
            try { await e2eTrainingRecord.WaiveRemedialPlanAsync(e2eSessionId, e2eWaiverPlanId, e2eWaiverApprovalId); }
            catch (InvalidOperationException) { e2eDoubleWaiveDenied = true; }
            AssertTrue(e2eDoubleWaiveDenied, "Waived remedial plans are closed and cannot be waived again");

            e2eRequirements = await e2eTrainingRecord.GetRemainingRequirementsAsync(e2eStudentId, e2eOrderId);
            e2eRequirementOne = e2eRequirements.Find(r => r.ObjectiveId == e2eObjectiveOneId)!;
            AssertEquals("Completed", e2eRequirementOne.Status, "Prior satisfactory evidence keeps the objective complete after a regression attempt");
            AssertEquals(2, e2eRequirementOne.Attempts, "Both attempts remain visible in the requirements view");
            AssertEquals("Unsatisfactory", e2eRequirementOne.LastResult, "The latest attempt result stays visible for examiners");
            AssertTrue(!e2eRequirementOne.HasOpenRemedial, "Waived remedial plans no longer block objectives");

            // Stage checks: append-only examination evidence
            bool e2eStageDeficienciesRequired = false;
            try
            {
                await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
                {
                    StudentId = e2eStudentId,
                    TrainingOrderId = e2eOrderId,
                    CurriculumVersionId = e2eVersionId,
                    StageCode = "STAGE-1",
                    ExaminerName = "Chief Examiner",
                    Result = StageCheck.ResultFail
                });
            }
            catch (ArgumentException) { e2eStageDeficienciesRequired = true; }
            AssertTrue(e2eStageDeficienciesRequired, "Failed stage checks must record deficiencies");

            int e2eStageFailId = await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
            {
                StudentId = e2eStudentId,
                TrainingOrderId = e2eOrderId,
                CurriculumVersionId = e2eVersionId,
                StageCode = "STAGE-1",
                ExaminerName = "Chief Examiner",
                Result = StageCheck.ResultFail,
                Deficiencies = "Steep turns below standard",
                RemedialReference = "Cross-control correction exercise",
                AssessedAt = new DateTime(2026, 2, 1, 9, 0, 0),
                TrainingSessionId = e2eScheduledSessionId
            });
            AssertTrue(e2eStageFailId > 0, "Failed stage check attempt is recorded");

            int e2eDraftVersionId = await e2eCurriculum.CreateCurriculumVersionAsync(e2eSessionId, e2ePart61TemplateId, "V-E2E-DRAFT", e2eEffectiveFrom, null, e2eDocumentId, "Unpublished draft for negative stage check");
            AssertEquals("Draft", await ReadE2EReviewStatusAsync("CurriculumVersions", e2eDraftVersionId), "Draft version remains unpublished");
            bool e2eDraftStageDenied = false;
            try
            {
                await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
                {
                    StudentId = e2eStudentId,
                    TrainingOrderId = e2eOrderId,
                    CurriculumVersionId = e2eDraftVersionId,
                    StageCode = "STAGE-1",
                    ExaminerName = "Chief Examiner",
                    Result = StageCheck.ResultFail,
                    Deficiencies = "Draft curriculum cannot be assessed"
                });
            }
            catch (InvalidOperationException) { e2eDraftStageDenied = true; }
            AssertTrue(e2eDraftStageDenied, "Stage checks require a published curriculum version");
            AssertEquals("Approved", await ReadE2EReviewStatusAsync("CurriculumVersions", e2eVersionId), "Creating a draft does not alter the published syllabus");

            bool e2eStageWrongStudentDenied = false;
            try
            {
                await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
                {
                    StudentId = e2eOtherStudentId,
                    TrainingOrderId = e2eOrderId,
                    CurriculumVersionId = e2eVersionId,
                    StageCode = "STAGE-1",
                    ExaminerName = "Chief Examiner",
                    Result = StageCheck.ResultFail,
                    Deficiencies = "Order ownership mismatch"
                });
            }
            catch (InvalidOperationException) { e2eStageWrongStudentDenied = true; }
            AssertTrue(e2eStageWrongStudentDenied, "Stage checks reject orders that belong to another trainee");

            bool e2eStageWrongSessionDenied = false;
            try
            {
                await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
                {
                    StudentId = e2eOtherStudentId,
                    TrainingOrderId = e2eOtherOrderId,
                    CurriculumVersionId = e2eVersionId,
                    StageCode = "STAGE-1",
                    ExaminerName = "Chief Examiner",
                    Result = StageCheck.ResultFail,
                    Deficiencies = "Session ownership mismatch",
                    TrainingSessionId = e2eScheduledSessionId
                });
            }
            catch (InvalidOperationException) { e2eStageWrongSessionDenied = true; }
            AssertTrue(e2eStageWrongSessionDenied, "Stage checks reject sessions that belong to another trainee");

            bool e2eStagePassEvidenceDenied = false;
            try
            {
                await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
                {
                    StudentId = e2eStudentId,
                    TrainingOrderId = e2eOrderId,
                    CurriculumVersionId = e2eVersionId,
                    StageCode = "STAGE-1",
                    ExaminerName = "Chief Examiner",
                    Result = StageCheck.ResultPass,
                    AssessedAt = new DateTime(2026, 2, 5, 9, 0, 0)
                });
            }
            catch (UnauthorizedAccessException) { e2eStagePassEvidenceDenied = true; }
            AssertTrue(e2eStagePassEvidenceDenied, "Passing a stage check requires password-verified approval evidence");

            int e2eStageApprovalRecordId = await e2eIdentity.RecordApprovalAsync(e2eSessionId, "StageCheck", e2eOrderId, "Accept:STAGE-1", "Stage check passed by chief examiner", e2eAdminPassword);
            bool e2eStageMismatchDenied = false;
            try
            {
                await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
                {
                    StudentId = e2eStudentId,
                    TrainingOrderId = e2eOrderId,
                    CurriculumVersionId = e2eVersionId,
                    StageCode = "STAGE-1",
                    ExaminerName = "Chief Examiner",
                    Result = StageCheck.ResultPass,
                    AssessedAt = new DateTime(2026, 2, 5, 9, 0, 0),
                    ApprovalRecordId = e2eStageApprovalRecordId + 7
                });
            }
            catch (UnauthorizedAccessException) { e2eStageMismatchDenied = true; }
            AssertTrue(e2eStageMismatchDenied, "Mismatched approval evidence is rejected for stage checks");

            int e2eStagePassId = await e2eTrainingRecord.RecordStageCheckAsync(e2eSessionId, new StageCheck
            {
                StudentId = e2eStudentId,
                TrainingOrderId = e2eOrderId,
                CurriculumVersionId = e2eVersionId,
                StageCode = "STAGE-1",
                ExaminerName = "Chief Examiner",
                Result = StageCheck.ResultPass,
                AssessedAt = new DateTime(2026, 2, 5, 9, 0, 0),
                TrainingSessionId = e2eScheduledSessionId,
                ApprovalRecordId = e2eStageApprovalRecordId
            });
            AssertTrue(e2eStagePassId > 0, "Passing stage check attempt is recorded with approval evidence");

            bool e2eStageUpdateRejected = false;
            bool e2eStageDeleteRejected = false;
            using (var e2eStageConnection = await e2eDatabase.OpenIdentityConnectionAsync())
            {
                using var e2eStageUpdate = e2eStageConnection.CreateCommand();
                e2eStageUpdate.CommandText = "UPDATE StageChecks SET Result = 'Pass' WHERE Id = @id;";
                e2eStageUpdate.Parameters.AddWithValue("@id", e2eStageFailId);
                try { await e2eStageUpdate.ExecuteNonQueryAsync(); }
                catch (SqliteException) { e2eStageUpdateRejected = true; }

                using var e2eStageDelete = e2eStageConnection.CreateCommand();
                e2eStageDelete.CommandText = "DELETE FROM StageChecks WHERE Id = @id;";
                e2eStageDelete.Parameters.AddWithValue("@id", e2eStageFailId);
                try { await e2eStageDelete.ExecuteNonQueryAsync(); }
                catch (SqliteException) { e2eStageDeleteRejected = true; }
            }
            AssertTrue(e2eStageUpdateRejected && e2eStageDeleteRejected, "Stage check attempts are append-only examination evidence");

            var e2eStageChecks = await e2eTrainingRecord.GetStageChecksAsync(e2eStudentId, e2eOrderId);
            AssertEquals(2, e2eStageChecks.Count, "Both stage check attempts are reconstructable");
            AssertEquals("Fail", e2eStageChecks[0].Result, "First attempt is the recorded failure");
            AssertEquals("Pass", e2eStageChecks[1].Result, "Second attempt is the recorded pass");
            AssertEquals(1, e2eStageChecks[0].AttemptNumber, "Attempt numbering starts at one");
            AssertEquals(2, e2eStageChecks[1].AttemptNumber, "Retakes increment the attempt counter");
            AssertEquals((int?)e2eStageApprovalRecordId, e2eStageChecks[1].ApprovalRecordId, "Passing attempts carry their approval evidence reference");
            AssertTrue(e2eStageChecks.TrueForAll(c => c.CurriculumVersionId == e2eVersionId), "Stage checks reference the governing curriculum version");

            e2ePlans = await e2eTrainingRecord.GetRemedialPlansAsync(e2eStudentId, e2eOrderId);
            AssertEquals(2, e2ePlans.Count, "Both remedial plans are reconstructable");
            AssertTrue(e2ePlans.Exists(p => p.Status == RemedialPlan.StatusCompleted), "Completed remediation remains in the record");
            AssertTrue(e2ePlans.Exists(p => p.Status == RemedialPlan.StatusWaived && p.ApprovalRecordId == e2eWaiverApprovalId), "Waived remediation carries its approval evidence");

            // Official training record (pre-graduation)
            var e2eRecord = await e2eTrainingRecord.GetOfficialTrainingRecordAsync(e2eStudentId, e2eOrderId);
            AssertEquals("E2E Curriculum Trainee", e2eRecord.StudentName, "Official record identifies the trainee");
            AssertEquals("E2E-001", e2eRecord.OrderNumber, "Official record identifies the training order");
            AssertEquals("Part61", e2eRecord.RegulatoryTrack, "Official record preserves the regulatory track");
            AssertEquals("قيد التدريب", e2eRecord.OrderStatus, "Order is active before graduation");
            AssertEquals("V-E2E-2026", e2eRecord.VersionLabel, "Official record binds to the governing curriculum version");
            AssertEquals(ReviewStatus.Approved, e2eRecord.VersionStatus, "Governing curriculum version is published");
            AssertEquals("ECAR-ECAA", e2eRecord.FrameworkCode, "Official record names the controlled regulatory framework");
            AssertEquals("Part-61", e2eRecord.DocumentPart, "Official record captures the controlled document part");
            AssertEquals("2026-02", e2eRecord.DocumentIssue, "Official record captures the controlled document issue");
            AssertEquals("R2", e2eRecord.DocumentRevision, "Official record captures the controlled document revision");
            AssertEquals("ECAA", e2eRecord.DocumentAuthority, "Official record captures the approving authority");
            AssertEquals(2, e2eRecord.Lessons.Count, "Official record lists both curriculum lessons");
            OfficialTrainingObjective? e2eOfficialOne = null;
            OfficialTrainingObjective? e2eOfficialTwo = null;
            int e2eOfficialObjectiveCount = 0;
            foreach (var e2eLesson in e2eRecord.Lessons)
            {
                foreach (var e2eOfficialObjective in e2eLesson.Objectives)
                {
                    e2eOfficialObjectiveCount++;
                    if (e2eOfficialObjective.ObjectiveId == e2eObjectiveOneId) e2eOfficialOne = e2eOfficialObjective;
                    if (e2eOfficialObjective.ObjectiveId == e2eObjectiveTwoId) e2eOfficialTwo = e2eOfficialObjective;
                }
            }
            AssertEquals(2, e2eOfficialObjectiveCount, "Official record lists both curriculum objectives");
            AssertTrue(e2eOfficialOne != null && e2eOfficialTwo != null, "Official record binds attempts to the correct objectives");
            AssertEquals(2, e2eOfficialOne!.Attempts.Count, "First objective keeps its complete attempt history");
            AssertEquals(2, e2eOfficialTwo!.Attempts.Count, "Second objective keeps its complete attempt history");
            AssertEquals("Completed", e2eOfficialOne.Status, "First objective is complete for graduation");
            AssertEquals("Completed", e2eOfficialTwo.Status, "Second objective is complete after remediation");
            AssertTrue(e2eOfficialOne.Attempts.TrueForAll(a => a.GraderUserId == e2eAdminId), "Every official attempt is attributable to the grading user");
            AssertEquals(2, e2eRecord.StageChecks.Count, "Official record includes both stage check attempts");
            AssertEquals(2, e2eRecord.RemedialPlans.Count, "Official record includes both remedial plans");

            // Graduation
            bool e2eCompleteWithoutApprovalDenied = false;
            try { await e2eDatabase.CompleteOrderAsync(e2eOrderId, new DateTime(2026, 6, 1)); }
            catch (UnauthorizedAccessException) { e2eCompleteWithoutApprovalDenied = true; }
            AssertTrue(e2eCompleteWithoutApprovalDenied, "Graduation requires password-verified approval evidence");

            bool e2eStillActive = false;
            using (var e2eActiveConnection = await e2eDatabase.OpenIdentityConnectionAsync())
            {
                using var e2eActiveCommand = e2eActiveConnection.CreateCommand();
                e2eActiveCommand.CommandText = "SELECT Status FROM TrainingOrders WHERE Id = @id;";
                e2eActiveCommand.Parameters.AddWithValue("@id", e2eOrderId);
                e2eStillActive = Convert.ToString(await e2eActiveCommand.ExecuteScalarAsync()) == "قيد التدريب";
            }
            AssertTrue(e2eStillActive, "Failed graduation attempt leaves the order active");

            int e2eCompletionApprovalId = await e2eIdentity.RecordApprovalAsync(e2eSessionId, "TrainingOrder", e2eOrderId, "Complete", "All curriculum requirements satisfied", e2eAdminPassword);
            AssertTrue(await e2eDatabase.CompleteOrderAsync(e2eOrderId, new DateTime(2026, 6, 1), "Curriculum end-to-end completed"), "Graduation completes the training order with verified evidence");

            string e2eFinalStatus = string.Empty;
            string e2eFinalCompletionDate = string.Empty;
            using (var e2eFinalConnection = await e2eDatabase.OpenIdentityConnectionAsync())
            {
                using var e2eFinalCommand = e2eFinalConnection.CreateCommand();
                e2eFinalCommand.CommandText = "SELECT Status, CompletionDate FROM TrainingOrders WHERE Id = @id;";
                e2eFinalCommand.Parameters.AddWithValue("@id", e2eOrderId);
                using var e2eFinalReader = await e2eFinalCommand.ExecuteReaderAsync();
                AssertTrue(await e2eFinalReader.ReadAsync(), "Graduated order row exists");
                e2eFinalStatus = e2eFinalReader.GetString(0);
                e2eFinalCompletionDate = e2eFinalReader.GetString(1);
            }
            AssertEquals("منتهي", e2eFinalStatus, "Graduated order carries the completed status");
            AssertEquals("2026-06-01", e2eFinalCompletionDate, "Graduated order carries the approved completion date");

            bool e2eSecondCompleteDenied = false;
            try { await e2eDatabase.CompleteOrderAsync(e2eOrderId, new DateTime(2026, 6, 2)); }
            catch (UnauthorizedAccessException) { e2eSecondCompleteDenied = true; }
            AssertTrue(e2eSecondCompleteDenied, "Graduation approval evidence is single-use");

            var e2eGraduatedRecord = await e2eTrainingRecord.GetOfficialTrainingRecordAsync(e2eStudentId, e2eOrderId);
            AssertEquals("منتهي", e2eGraduatedRecord.OrderStatus, "Official record reflects graduation");
            AssertEquals("V-E2E-2026", e2eGraduatedRecord.VersionLabel, "Official record still binds to the governing curriculum version");
            AssertEquals(2, e2eGraduatedRecord.StageChecks.Count, "Official record preserves stage check evidence after graduation");
            AssertEquals(2, e2eGraduatedRecord.RemedialPlans.Count, "Official record preserves remediation evidence after graduation");

            // Official record Excel export (RTL, bilingual, signature block)
            string e2eExportPath = await e2eTrainingRecord.ExportOfficialTrainingRecordAsync(Path.Combine(_testDataDirectory, "curriculum-e2e", "official_training_record.xlsx"), e2eStudentId, e2eOrderId);
            AssertTrue(File.Exists(e2eExportPath), "Official training record exports to an Excel file");
            using (var e2eExportWorkbook = new XLWorkbook(e2eExportPath))
            {
                var e2eExportSheet = e2eExportWorkbook.Worksheet(1);
                AssertTrue(e2eExportSheet.RightToLeft, "Official record worksheet is Right-To-Left");
                var e2eExportTexts = new List<string>();
                var e2eUsedRange = e2eExportSheet.RangeUsed()!;
                foreach (var e2eCell in e2eUsedRange.CellsUsed())
                    e2eExportTexts.Add(e2eCell.GetString());
                string e2eExportCombined = string.Join(" | ", e2eExportTexts);
                AssertTrue(e2eExportCombined.Contains("E2E Curriculum Trainee"), "Export identifies the trainee");
                AssertTrue(e2eExportCombined.Contains("E2E-001"), "Export identifies the training order");
                AssertTrue(e2eExportCombined.Contains("V-E2E-2026"), "Export binds the governing curriculum version");
                AssertTrue(e2eExportCombined.Contains("OBJ-E1") && e2eExportCombined.Contains("OBJ-E2"), "Export lists both curriculum objectives");
                AssertTrue(e2eExportCombined.Contains("R2"), "Export captures the controlled document revision");
                AssertTrue(e2eExportCombined.Contains("Chief Examiner"), "Export lists the examiner signature");
                AssertTrue(e2eExportCombined.Contains("Pass") && e2eExportCombined.Contains("Fail"), "Export includes both stage check results");
                AssertTrue(e2eExportCombined.Contains("Waived") && e2eExportCombined.Contains("Completed"), "Export includes remedial plan outcomes");
                AssertTrue(e2eExportCombined.Contains("Not acknowledged"), "Export shows the pending student signature until acknowledged");
            }

            // Hour reconciliation: syllabus requirements vs sessions vs flight records
            using (var e2eHoursConnection = await e2eDatabase.OpenIdentityConnectionAsync())
            {
                using var e2eSyllabusCommand = e2eHoursConnection.CreateCommand();
                e2eSyllabusCommand.CommandText = "UPDATE TrainingOrders SET SyllabusHours = 40.0 WHERE Id = @orderId;";
                e2eSyllabusCommand.Parameters.AddWithValue("@orderId", e2eOrderId);
                await e2eSyllabusCommand.ExecuteNonQueryAsync();

                using var e2eCleanFlightCommand = e2eHoursConnection.CreateCommand();
                e2eCleanFlightCommand.CommandText = @"
                    INSERT INTO FlightRecords (TrainingSessionId, StudentId, ActivityType, ResourceName, InstructorName, Route, StartAt, EndAt, HobbsStart, HobbsEnd, Landings, Remarks, CreatedAt)
                    VALUES (@sessionId, @studentId, 'Dual', 'SU-E2E', 'CFI E2E', 'VFR Local', @startAt, @endAt, 100.0, 101.5, 1, 'Clean linked record', @createdAt);";
                e2eCleanFlightCommand.Parameters.AddWithValue("@sessionId", e2eScheduledSessionId);
                e2eCleanFlightCommand.Parameters.AddWithValue("@studentId", e2eStudentId);
                e2eCleanFlightCommand.Parameters.AddWithValue("@startAt", new DateTime(2026, 1, 10, 8, 0, 0));
                e2eCleanFlightCommand.Parameters.AddWithValue("@endAt", new DateTime(2026, 1, 10, 9, 30, 0));
                e2eCleanFlightCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
                await e2eCleanFlightCommand.ExecuteNonQueryAsync();
            }

            var e2eCleanReconciliation = await e2eTrainingRecord.ReconcileTrainingHoursAsync(e2eStudentId, e2eOrderId);
            AssertEquals(40.0, e2eCleanReconciliation.RequiredHours, "Reconciliation reads the order syllabus hours");
            AssertEquals(2.0, e2eCleanReconciliation.ScheduledSessionHours, "Reconciliation totals scheduled session hours inside the order window");
            AssertEquals(0.0, e2eCleanReconciliation.CompletedSessionHours, "No completed sessions exist in the clean scenario");
            AssertEquals(1.5, e2eCleanReconciliation.FlightRecordHours, "Reconciliation totals linked flight record hours");
            AssertEquals(1.5, e2eCleanReconciliation.HobbsRecordedHours, "Reconciliation totals hobbs hours");
            AssertEquals(38.5, e2eCleanReconciliation.RemainingRequiredHours, "Reconciliation computes remaining required hours");
            AssertEquals(0, e2eCleanReconciliation.Issues.Count, "A clean flight record set produces no reconciliation issues");
            AssertTrue(e2eCleanReconciliation.IsReconciled, "A clean flight record set reconciles");

            int e2eMismatchSessionId = 0;
            using (var e2eIssueConnection = await e2eDatabase.OpenIdentityConnectionAsync())
            {
                using var e2eBadWindowCommand = e2eIssueConnection.CreateCommand();
                e2eBadWindowCommand.CommandText = @"
                    INSERT INTO TrainingSessions (StudentId, RegulatoryTrack, LessonTitle, InstructorName, ResourceName, Location, StartAt, EndAt, Status, Notes, CreatedAt)
                    VALUES (@studentId, 'Part61', 'E2E Bad Window Lesson', 'CFI E2E', 'SU-E2E', 'October Airport', @startAt, @endAt, 'Scheduled', 'Bad window fixture', @createdAt);";
                e2eBadWindowCommand.Parameters.AddWithValue("@studentId", e2eStudentId);
                e2eBadWindowCommand.Parameters.AddWithValue("@startAt", new DateTime(2026, 2, 1, 9, 0, 0));
                e2eBadWindowCommand.Parameters.AddWithValue("@endAt", new DateTime(2026, 2, 1, 8, 0, 0));
                e2eBadWindowCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
                await e2eBadWindowCommand.ExecuteNonQueryAsync();

                using var e2eOtherStudentSessionCommand = e2eIssueConnection.CreateCommand();
                e2eOtherStudentSessionCommand.CommandText = @"
                    INSERT INTO TrainingSessions (StudentId, RegulatoryTrack, LessonTitle, InstructorName, ResourceName, Location, StartAt, EndAt, Status, Notes, CreatedAt)
                    VALUES (@studentId, 'Part61', 'E2E Other Trainee Lesson', 'CFI E2E', 'SU-E2E', 'October Airport', @startAt, @endAt, 'Completed', 'Ownership mismatch fixture', @createdAt);";
                e2eOtherStudentSessionCommand.Parameters.AddWithValue("@studentId", e2eOtherStudentId);
                e2eOtherStudentSessionCommand.Parameters.AddWithValue("@startAt", new DateTime(2026, 3, 5, 8, 0, 0));
                e2eOtherStudentSessionCommand.Parameters.AddWithValue("@endAt", new DateTime(2026, 3, 5, 9, 0, 0));
                e2eOtherStudentSessionCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
                await e2eOtherStudentSessionCommand.ExecuteNonQueryAsync();
                using var e2eLastSessionCommand = e2eIssueConnection.CreateCommand();
                e2eLastSessionCommand.CommandText = "SELECT last_insert_rowid();";
                e2eMismatchSessionId = Convert.ToInt32(await e2eLastSessionCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
                AssertTrue(e2eMismatchSessionId > 0, "Ownership mismatch session fixture is created");

                using var e2eUnlinkedVarianceCommand = e2eIssueConnection.CreateCommand();
                e2eUnlinkedVarianceCommand.CommandText = @"
                    INSERT INTO FlightRecords (TrainingSessionId, StudentId, ActivityType, ResourceName, InstructorName, Route, StartAt, EndAt, HobbsStart, HobbsEnd, Landings, Remarks, CreatedAt)
                    VALUES (NULL, @studentId, 'Dual', 'SU-E2E', 'CFI E2E', 'VFR Local', @startAt, @endAt, 101.5, 103.0, 1, 'Unlinked hobbs variance fixture', @createdAt);";
                e2eUnlinkedVarianceCommand.Parameters.AddWithValue("@studentId", e2eStudentId);
                e2eUnlinkedVarianceCommand.Parameters.AddWithValue("@startAt", new DateTime(2026, 1, 15, 10, 0, 0));
                e2eUnlinkedVarianceCommand.Parameters.AddWithValue("@endAt", new DateTime(2026, 1, 15, 11, 0, 0));
                e2eUnlinkedVarianceCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
                await e2eUnlinkedVarianceCommand.ExecuteNonQueryAsync();

                using var e2eMismatchFlightCommand = e2eIssueConnection.CreateCommand();
                e2eMismatchFlightCommand.CommandText = @"
                    INSERT INTO FlightRecords (TrainingSessionId, StudentId, ActivityType, ResourceName, InstructorName, Route, StartAt, EndAt, HobbsStart, HobbsEnd, Landings, Remarks, CreatedAt)
                    VALUES (@sessionId, @studentId, 'Dual', 'SU-E2E', 'CFI E2E', 'VFR Local', @startAt, @endAt, 103.0, 103.5, 1, 'Ownership mismatch fixture', @createdAt);";
                e2eMismatchFlightCommand.Parameters.AddWithValue("@sessionId", e2eMismatchSessionId);
                e2eMismatchFlightCommand.Parameters.AddWithValue("@studentId", e2eStudentId);
                e2eMismatchFlightCommand.Parameters.AddWithValue("@startAt", new DateTime(2026, 3, 5, 8, 0, 0));
                e2eMismatchFlightCommand.Parameters.AddWithValue("@endAt", new DateTime(2026, 3, 5, 8, 30, 0));
                e2eMismatchFlightCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
                await e2eMismatchFlightCommand.ExecuteNonQueryAsync();

                using var e2eZeroWindowCommand = e2eIssueConnection.CreateCommand();
                e2eZeroWindowCommand.CommandText = @"
                    INSERT INTO FlightRecords (TrainingSessionId, StudentId, ActivityType, ResourceName, InstructorName, Route, StartAt, EndAt, HobbsStart, HobbsEnd, Landings, Remarks, CreatedAt)
                    VALUES (NULL, @studentId, 'Dual', 'SU-E2E', 'CFI E2E', 'VFR Local', @startAt, @endAt, 103.5, 103.5, 0, 'Zero window fixture', @createdAt);";
                e2eZeroWindowCommand.Parameters.AddWithValue("@studentId", e2eStudentId);
                e2eZeroWindowCommand.Parameters.AddWithValue("@startAt", new DateTime(2026, 1, 20, 12, 0, 0));
                e2eZeroWindowCommand.Parameters.AddWithValue("@endAt", new DateTime(2026, 1, 20, 12, 0, 0));
                e2eZeroWindowCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
                await e2eZeroWindowCommand.ExecuteNonQueryAsync();
            }

            var e2eReconciliation = await e2eTrainingRecord.ReconcileTrainingHoursAsync(e2eStudentId, e2eOrderId);
            var e2eIssueCodes = new List<string>();
            foreach (var e2eIssue in e2eReconciliation.Issues)
                e2eIssueCodes.Add(e2eIssue.Code);
            AssertTrue(e2eIssueCodes.Contains("SessionWindow"), "Reconciliation flags invalid session windows");
            AssertTrue(e2eIssueCodes.Contains("HobbsVariance"), "Reconciliation flags hobbs vs elapsed variance");
            AssertTrue(e2eIssueCodes.Contains("UnlinkedFlightRecord"), "Reconciliation flags unlinked flight records");
            AssertTrue(e2eIssueCodes.Contains("SessionStudentMismatch"), "Reconciliation flags records linked to another student's session");
            AssertTrue(!e2eReconciliation.IsReconciled, "Discrepant hours do not reconcile");
            AssertEquals(3.0, e2eReconciliation.FlightRecordHours, "Flight hours include every in-window record");
            AssertEquals(3.5, e2eReconciliation.HobbsRecordedHours, "Hobbs hours include every in-window record");
            AssertEquals(37.0, e2eReconciliation.RemainingRequiredHours, "Remaining hours account for all flight evidence");
            AssertEquals(2.0, e2eReconciliation.ScheduledSessionHours, "Bad-window sessions are excluded from scheduled totals");

            bool e2eHoursWrongStudentDenied = false;
            try { await e2eTrainingRecord.ReconcileTrainingHoursAsync(e2eOtherStudentId, e2eOrderId); }
            catch (InvalidOperationException) { e2eHoursWrongStudentDenied = true; }
            AssertTrue(e2eHoursWrongStudentDenied, "Reconciliation rejects an order owned by another student");

            bool e2eHoursUnknownOrderDenied = false;
            try { await e2eTrainingRecord.ReconcileTrainingHoursAsync(e2eStudentId, 999999); }
            catch (InvalidOperationException) { e2eHoursUnknownOrderDenied = true; }
            AssertTrue(e2eHoursUnknownOrderDenied, "Reconciliation rejects an unknown training order");

            // Student electronic signature on the official training record
            bool e2eAdminAckDenied = false;
            try { await e2eTrainingRecord.AcknowledgeOfficialTrainingRecordAsync(e2eSessionId, e2eStudentId, e2eOrderId, "Administrator attempts student acknowledgment", e2eAdminPassword); }
            catch (InvalidOperationException) { e2eAdminAckDenied = true; }
            AssertTrue(e2eAdminAckDenied, "An account without a student link cannot acknowledge a training record");

            const string e2eTraineePassword = "E2E-Trainee-Password-2026!";
            int e2eTraineeUserId = await e2eIdentity.CreateUserAsync(e2eSessionId, "e2e-trainee", "E2E Trainee Account", e2eTraineePassword, new[] { "student" }, new[] { "OCT" });
            AssertTrue(e2eTraineeUserId > 0, "Student account for record acknowledgment is created");
            using (var e2eLinkConnection = await e2eDatabase.OpenIdentityConnectionAsync())
            {
                using var e2eLinkCommand = e2eLinkConnection.CreateCommand();
                e2eLinkCommand.CommandText = "UPDATE Users SET StudentId = @studentId WHERE Id = @userId;";
                e2eLinkCommand.Parameters.AddWithValue("@studentId", e2eStudentId);
                e2eLinkCommand.Parameters.AddWithValue("@userId", e2eTraineeUserId);
                await e2eLinkCommand.ExecuteNonQueryAsync();
            }

            var e2eTraineeSession = await e2eIdentity.AuthenticateAsync("e2e-trainee", e2eTraineePassword, "OCT");
            AssertTrue(e2eTraineeSession != null, "Student can authenticate to acknowledge the official record");
            string e2eTraineeSessionId = e2eTraineeSession!.SessionId;

            bool e2eAckOtherOrderDenied = false;
            try { await e2eTrainingRecord.AcknowledgeOfficialTrainingRecordAsync(e2eTraineeSessionId, e2eStudentId, e2eOtherOrderId, "Acknowledging an order owned by another trainee", e2eTraineePassword); }
            catch (InvalidOperationException) { e2eAckOtherOrderDenied = true; }
            AssertTrue(e2eAckOtherOrderDenied, "A student cannot acknowledge an order owned by another student");

            bool e2eAckWrongStudentDenied = false;
            try { await e2eTrainingRecord.AcknowledgeOfficialTrainingRecordAsync(e2eTraineeSessionId, e2eOtherStudentId, e2eOrderId, "Acknowledging under a different student identity", e2eTraineePassword); }
            catch (UnauthorizedAccessException) { e2eAckWrongStudentDenied = true; }
            AssertTrue(e2eAckWrongStudentDenied, "Acknowledgment is rejected when the student identity does not match the account link");

            bool e2eAckShortReasonDenied = false;
            try { await e2eTrainingRecord.AcknowledgeOfficialTrainingRecordAsync(e2eTraineeSessionId, e2eStudentId, e2eOrderId, "short", e2eTraineePassword); }
            catch (ArgumentException) { e2eAckShortReasonDenied = true; }
            AssertTrue(e2eAckShortReasonDenied, "Acknowledgment requires a substantive reason");

            bool e2eAckWrongPasswordDenied = false;
            try { await e2eTrainingRecord.AcknowledgeOfficialTrainingRecordAsync(e2eTraineeSessionId, e2eStudentId, e2eOrderId, "Re-authentication with the wrong password", "Wrong-Password-2026!"); }
            catch (UnauthorizedAccessException) { e2eAckWrongPasswordDenied = true; }
            AssertTrue(e2eAckWrongPasswordDenied, "Acknowledgment requires the student's own password");

            int e2eAcknowledgmentId = await e2eTrainingRecord.AcknowledgeOfficialTrainingRecordAsync(e2eTraineeSessionId, e2eStudentId, e2eOrderId, "Trainee received and verified the official training record", e2eTraineePassword);
            AssertTrue(e2eAcknowledgmentId > 0, "Student acknowledgment of the official training record is recorded");

            bool e2eAckTwiceDenied = false;
            try { await e2eTrainingRecord.AcknowledgeOfficialTrainingRecordAsync(e2eTraineeSessionId, e2eStudentId, e2eOrderId, "Attempting a duplicate acknowledgment", e2eTraineePassword); }
            catch (InvalidOperationException) { e2eAckTwiceDenied = true; }
            AssertTrue(e2eAckTwiceDenied, "A training record cannot be acknowledged twice");

            var e2eAdminSessionAgain = await e2eIdentity.AuthenticateAsync("eaa-admin", e2eAdminPassword, "OCT");
            AssertTrue(e2eAdminSessionAgain != null, "Administrator session re-opens after student acknowledgment");

            string e2eSignedExportPath = await e2eTrainingRecord.ExportOfficialTrainingRecordAsync(Path.Combine(_testDataDirectory, "curriculum-e2e", "official_training_record_signed.xlsx"), e2eStudentId, e2eOrderId);
            AssertTrue(File.Exists(e2eSignedExportPath), "Signed official record exports to an Excel file");
            using (var e2eSignedWorkbook = new XLWorkbook(e2eSignedExportPath))
            {
                var e2eSignedSheet = e2eSignedWorkbook.Worksheet(1);
                var e2eSignedTexts = new List<string>();
                var e2eSignedRange = e2eSignedSheet.RangeUsed()!;
                foreach (var e2eSignedCell in e2eSignedRange.CellsUsed())
                    e2eSignedTexts.Add(e2eSignedCell.GetString());
                string e2eSignedCombined = string.Join(" | ", e2eSignedTexts);
                AssertTrue(e2eSignedCombined.Contains("E2E Trainee Account"), "Signed export shows the student's electronic signature");
                AssertTrue(!e2eSignedCombined.Contains("Not acknowledged"), "Signed export no longer shows a pending student signature");
            }
            Console.WriteLine("  ✔ Full curriculum end-to-end (session → remediation → stage check → graduation → official record) verified!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✖ TEST SUITE 23 FAILED: {ex.Message}");
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

        if (failures == 0)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(_testDataDirectory, recursive: true);
                Console.WriteLine("Isolated verification data cleaned up.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not clean isolated verification data at '{_testDataDirectory}': {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"Isolated verification data retained for diagnosis: {_testDataDirectory}");
        }

        return failures;
    }

    private static string FindWorkbookFixture()
    {
        // Tests run from bin/Debug, while the workbook fixture lives at the repository root.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
        {
            foreach (var file in directory.GetFiles("*.xlsx"))
            {
                if (file.Length > 50_000 && !file.Name.StartsWith("Test_", StringComparison.OrdinalIgnoreCase))
                    return file.FullName;
            }
        }

        throw new FileNotFoundException("Excel fixture workbook was not found above the test output directory.");
    }

    private static async Task<HashSet<string>> ReadSqliteColumnsAsync(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        return columns;
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
