using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EAATrainingManager.Helpers;
using EAATrainingManager.Models;
using Microsoft.Data.Sqlite;

namespace EAATrainingManager.Services;

public class DatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(localAppData, "EAA_TrainingManager");
        Directory.CreateDirectory(appFolder);

        _dbPath = Path.Combine(appFolder, "eaa_training.db");
        _connectionString = $"Data Source={_dbPath};";
    }

    public string GetDatabasePath() => _dbPath;
    public string DatabasePath => _dbPath;

    public async Task InitializeAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Students (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NormalizedName TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                Nationality TEXT NOT NULL DEFAULT 'مصري',
                IsInternational INTEGER NOT NULL DEFAULT 0,
                NationalId TEXT,
                Phone TEXT,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TrainingOrders (
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
                AcademicYear INTEGER NOT NULL DEFAULT 2026,
                RegulatoryTrack TEXT NOT NULL DEFAULT 'Part61',
                SequenceNumber INTEGER NOT NULL,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_students_norm ON Students(NormalizedName);
            CREATE INDEX IF NOT EXISTS idx_orders_student ON TrainingOrders(StudentId);
            CREATE INDEX IF NOT EXISTS idx_orders_num_yr ON TrainingOrders(OrderNumber, Year);
            CREATE INDEX IF NOT EXISTS idx_orders_year ON TrainingOrders(AcademicYear);
            CREATE INDEX IF NOT EXISTS idx_orders_track ON TrainingOrders(RegulatoryTrack);
            CREATE INDEX IF NOT EXISTS idx_orders_status ON TrainingOrders(Status);
            CREATE INDEX IF NOT EXISTS idx_orders_milestone ON TrainingOrders(Milestone);
        ";
        await cmd.ExecuteNonQueryAsync();

        // Non-destructive column auto-migrations for existing databases
        try
        {
            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA table_info(Students);";
            var studentCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var r = await pragmaCmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync()) studentCols.Add(r.GetString(1));
            }
            if (!studentCols.Contains("IsInternational"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Students ADD COLUMN IsInternational INTEGER NOT NULL DEFAULT 0;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!studentCols.Contains("IsArchived"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Students ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!studentCols.Contains("ArchivedAt"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Students ADD COLUMN ArchivedAt TEXT;";
                await alterCmd.ExecuteNonQueryAsync();
            }

            pragmaCmd.CommandText = "PRAGMA table_info(TrainingOrders);";
            var orderCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var r = await pragmaCmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync()) orderCols.Add(r.GetString(1));
            }
            if (!orderCols.Contains("AcademicYear"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE TrainingOrders ADD COLUMN AcademicYear INTEGER NOT NULL DEFAULT 2026;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!orderCols.Contains("RegulatoryTrack"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE TrainingOrders ADD COLUMN RegulatoryTrack TEXT NOT NULL DEFAULT 'Part61';";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!orderCols.Contains("IsArchived"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE TrainingOrders ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!orderCols.Contains("ArchivedAt"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE TrainingOrders ADD COLUMN ArchivedAt TEXT;";
                await alterCmd.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            // Ignore migration exceptions if columns already exist
        }
    }

    /// <summary>
    /// Decouples student identity from course enrollments:
    /// Finds or creates a single canonical student record by normalized Arabic name.
    /// </summary>
    public async Task<int> GetOrCreateStudentAsync(string displayName, string nationality = "مصري", string? nationalId = null, string? phone = null)
    {
        string cleanDisplay = ArabicTextHelper.CleanDisplayName(displayName);
        string normalized = ArabicTextHelper.Normalize(cleanDisplay);

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Student name cannot be empty", nameof(displayName));

        string cleanNat = DemographicsEngine.StandardizeNationality(nationality);
        bool isInternational = DemographicsEngine.ClassifyIfInternational(cleanNat);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 1. Check if student already exists
        using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = "SELECT Id, Nationality, IsInternational FROM Students WHERE NormalizedName = @norm LIMIT 1;";
            selectCmd.Parameters.AddWithValue("@norm", normalized);
            using var reader = await selectCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int studentId = reader.GetInt32(0);
                string existingNat = reader.IsDBNull(1) ? "مصري" : reader.GetString(1);
                int existingIsIntl = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

                // If currently stored as domestic or blank, but incoming nationality is international, upgrade it
                if (isInternational && (existingIsIntl == 0 || existingNat == "مصري"))
                {
                    using var updateCmd = connection.CreateCommand();
                    updateCmd.CommandText = "UPDATE Students SET Nationality = @nat, IsInternational = 1 WHERE Id = @id;";
                    updateCmd.Parameters.AddWithValue("@nat", cleanNat);
                    updateCmd.Parameters.AddWithValue("@id", studentId);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                return studentId;
            }
        }

        // 2. Insert new student record
        using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.CommandText = @"
                INSERT INTO Students (NormalizedName, DisplayName, Nationality, IsInternational, NationalId, Phone, CreatedAt)
                VALUES (@norm, @display, @nat, @isIntl, @natId, @phone, @created);
                SELECT last_insert_rowid();
            ";
            insertCmd.Parameters.AddWithValue("@norm", normalized);
            insertCmd.Parameters.AddWithValue("@display", cleanDisplay);
            insertCmd.Parameters.AddWithValue("@nat", cleanNat);
            insertCmd.Parameters.AddWithValue("@isIntl", isInternational ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@natId", (object?)nationalId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@phone", (object?)phone ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));

            var result = await insertCmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
    }

    /// <summary>
    /// Inserts or updates a training order for a student.
    /// </summary>
    public async Task<int> InsertOrUpdateOrderAsync(TrainingOrder order)
    {
        // Standardize milestone classification
        order.Milestone = ClassifyMilestone(order.ProgramType, order.Notes);

        if (order.AcademicYear <= 0)
        {
            order.AcademicYear = order.Year > 0 ? order.Year : (order.EnrollmentDate.HasValue ? order.EnrollmentDate.Value.Year : 2026);
        }

        if (order.Year <= 0)
        {
            order.Year = order.AcademicYear;
        }

        if (string.IsNullOrWhiteSpace(order.RegulatoryTrack) || order.RegulatoryTrack == "Part61")
        {
            var classified = ClassifyRegulatoryTrack(order.RegulationCategory, order.ProgramType);
            if (!string.IsNullOrWhiteSpace(classified))
            {
                order.RegulatoryTrack = classified;
            }
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Check if this exact order number and student already exists
        int existingId = 0;
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = @"
                SELECT Id FROM TrainingOrders 
                WHERE StudentId = @sid AND OrderNumber = @ordNum AND (Year = @yr OR AcademicYear = @acadYr)
                LIMIT 1;
            ";
            checkCmd.Parameters.AddWithValue("@sid", order.StudentId);
            checkCmd.Parameters.AddWithValue("@ordNum", order.OrderNumber);
            checkCmd.Parameters.AddWithValue("@yr", order.Year);
            checkCmd.Parameters.AddWithValue("@acadYr", order.AcademicYear);

            var res = await checkCmd.ExecuteScalarAsync();
            if (res != null && res != DBNull.Value)
            {
                existingId = Convert.ToInt32(res);
            }
        }

        using var cmd = connection.CreateCommand();
        if (existingId > 0)
        {
            cmd.CommandText = @"
                UPDATE TrainingOrders SET 
                    ProgramType = @prog,
                    Milestone = @milestone,
                    RegulationCategory = @cat,
                    EnrollmentDate = @enroll,
                    CompletionDate = @complete,
                    Notes = @notes,
                    Status = @status,
                    AcademicYear = @acadYr,
                    RegulatoryTrack = @regTrack,
                    SequenceNumber = @seq
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", existingId);
        }
        else
        {
            cmd.CommandText = @"
                INSERT INTO TrainingOrders 
                (StudentId, OrderNumber, ProgramType, Milestone, RegulationCategory, EnrollmentDate, CompletionDate, Notes, Status, Year, AcademicYear, RegulatoryTrack, SequenceNumber)
                VALUES 
                (@sid, @ordNum, @prog, @milestone, @cat, @enroll, @complete, @notes, @status, @yr, @acadYr, @regTrack, @seq);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@sid", order.StudentId);
            cmd.Parameters.AddWithValue("@ordNum", order.OrderNumber);
            cmd.Parameters.AddWithValue("@yr", order.Year);
        }

        cmd.Parameters.AddWithValue("@prog", order.ProgramType);
        cmd.Parameters.AddWithValue("@milestone", order.Milestone);
        cmd.Parameters.AddWithValue("@cat", order.RegulationCategory);
        cmd.Parameters.AddWithValue("@enroll", order.EnrollmentDate.HasValue ? order.EnrollmentDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@complete", order.CompletionDate.HasValue ? order.CompletionDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)order.Notes ?? string.Empty);
        cmd.Parameters.AddWithValue("@status", order.Status);
        cmd.Parameters.AddWithValue("@acadYr", order.AcademicYear);
        cmd.Parameters.AddWithValue("@regTrack", order.RegulatoryTrack);
        cmd.Parameters.AddWithValue("@seq", order.SequenceNumber);

        if (existingId > 0)
        {
            await cmd.ExecuteNonQueryAsync();
            return existingId;
        }
        else
        {
            var newId = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(newId);
        }
    }

    /// <summary>
    /// Standardizes the regulatory track (Part 61, Part 141, Evaluation, TypeRating)
    /// </summary>
    public static string ClassifyRegulatoryTrack(string category, string programType)
    {
        string combined = $"{category} {programType}".ToLowerInvariant();
        if (combined.Contains("61") || combined.Contains("حر")) return "Part61";
        if (combined.Contains("141") || combined.Contains("معتمد") || combined.Contains("دفعة") || combined.Contains("دفعات")) return "Part141";
        if (combined.Contains("تقييم") || combined.Contains("معادلة")) return "Evaluation";
        if (combined.Contains("طراز") || combined.Contains("فرق") || combined.Contains("بناء ساعات")) return "TypeRating";
        if (combined.Contains("خط جوي") || combined.Contains("atp")) return "Part141";
        return "Part61";
    }

    /// <summary>
    /// Retrieves all students with aggregated counts and milestone completion badges.
    /// Supports Arabic fuzzy and prefix-insensitive search, and academic year / nationality scoping.
    /// </summary>
    public async Task<List<Student>> GetAllStudentsAsync(string? searchQuery = null, string? statusFilter = null, int? academicYear = null, bool? internationalOnly = null)
    {
        var students = new List<Student>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        if (academicYear.HasValue && academicYear.Value > 0)
        {
            cmd.CommandText = @"
                SELECT 
                    s.Id, s.NormalizedName, s.DisplayName, s.Nationality, s.IsInternational, s.NationalId, s.Phone, s.CreatedAt,
                    COUNT(o.Id) AS TotalOrders,
                    SUM(CASE WHEN o.CompletionDate IS NULL OR o.CompletionDate = '' THEN 1 ELSE 0 END) AS ActiveOrders,
                    SUM(CASE WHEN o.CompletionDate IS NOT NULL AND o.CompletionDate != '' THEN 1 ELSE 0 END) AS CompletedOrders,
                    SUM(CASE WHEN o.Milestone = 'PPL' THEN 1 ELSE 0 END) AS HasPPL,
                    SUM(CASE WHEN o.Milestone = 'CPL_IR' THEN 1 ELSE 0 END) AS HasCPLIR,
                    SUM(CASE WHEN o.Milestone = 'ATP' THEN 1 ELSE 0 END) AS HasATP,
                    SUM(CASE WHEN o.Milestone = 'EVALUATION' THEN 1 ELSE 0 END) AS HasEval
                FROM Students s
                INNER JOIN TrainingOrders o ON s.Id = o.StudentId
                WHERE (s.IsArchived = 0 OR s.IsArchived IS NULL) 
                  AND (o.IsArchived = 0 OR o.IsArchived IS NULL)
                  AND (o.AcademicYear = @acadYr OR o.Year = @acadYr)
                GROUP BY s.Id
                ORDER BY s.DisplayName COLLATE NOCASE;
            ";
            cmd.Parameters.AddWithValue("@acadYr", academicYear.Value);
        }
        else
        {
            cmd.CommandText = @"
                SELECT 
                    s.Id, s.NormalizedName, s.DisplayName, s.Nationality, s.IsInternational, s.NationalId, s.Phone, s.CreatedAt,
                    COUNT(o.Id) AS TotalOrders,
                    SUM(CASE WHEN o.CompletionDate IS NULL OR o.CompletionDate = '' THEN 1 ELSE 0 END) AS ActiveOrders,
                    SUM(CASE WHEN o.CompletionDate IS NOT NULL AND o.CompletionDate != '' THEN 1 ELSE 0 END) AS CompletedOrders,
                    SUM(CASE WHEN o.Milestone = 'PPL' THEN 1 ELSE 0 END) AS HasPPL,
                    SUM(CASE WHEN o.Milestone = 'CPL_IR' THEN 1 ELSE 0 END) AS HasCPLIR,
                    SUM(CASE WHEN o.Milestone = 'ATP' THEN 1 ELSE 0 END) AS HasATP,
                    SUM(CASE WHEN o.Milestone = 'EVALUATION' THEN 1 ELSE 0 END) AS HasEval
                FROM Students s
                LEFT JOIN TrainingOrders o ON s.Id = o.StudentId AND (o.IsArchived = 0 OR o.IsArchived IS NULL)
                WHERE (s.IsArchived = 0 OR s.IsArchived IS NULL)
                GROUP BY s.Id
                ORDER BY s.DisplayName COLLATE NOCASE;
            ";
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string nat = reader.IsDBNull(3) ? "مصري" : reader.GetString(3);
            bool isIntl = (reader.IsDBNull(4) ? 0 : reader.GetInt32(4)) == 1 || DemographicsEngine.ClassifyIfInternational(nat);

            var student = new Student
            {
                Id = reader.GetInt32(0),
                NormalizedName = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Nationality = nat,
                IsInternational = isIntl,
                NationalId = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Phone = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                CreatedAt = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.Now,
                TotalOrdersCount = reader.GetInt32(8),
                ActiveOrdersCount = reader.GetInt32(9),
                CompletedOrdersCount = reader.GetInt32(10),
                HasPPL = reader.GetInt32(11) > 0,
                HasCPLIR = reader.GetInt32(12) > 0,
                HasATP = reader.GetInt32(13) > 0,
                HasEvaluation = reader.GetInt32(14) > 0
            };

            // Filter international only if requested
            if (internationalOnly.HasValue && internationalOnly.Value && !student.IsInternational)
                continue;

            // Filter by overall status if specified
            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "الجميع")
            {
                if (statusFilter == "قيد التدريب" && student.OverallStatus != "قيد التدريب")
                    continue;
                if (statusFilter == "خريج" && student.OverallStatus != "خريج")
                    continue;
            }

            // Fuzzy prefix-insensitive Arabic search
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                if (!ArabicTextHelper.IsFuzzyMatch(student.DisplayName, searchQuery) &&
                    !student.NormalizedName.Contains(ArabicTextHelper.Normalize(searchQuery)))
                {
                    continue;
                }
            }

            students.Add(student);
        }

        return students;
    }

    /// <summary>
    /// Fetches a single student along with their full 360° chronological trajectory of orders.
    /// </summary>
    public async Task<Student?> GetStudentWithTrajectoryAsync(int studentId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        Student? student = null;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, NormalizedName, DisplayName, Nationality, IsInternational, NationalId, Phone, CreatedAt FROM Students WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", studentId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                string nat = reader.IsDBNull(3) ? "مصري" : reader.GetString(3);
                bool isIntl = (reader.IsDBNull(4) ? 0 : reader.GetInt32(4)) == 1 || DemographicsEngine.ClassifyIfInternational(nat);
                student = new Student
                {
                    Id = reader.GetInt32(0),
                    NormalizedName = reader.GetString(1),
                    DisplayName = reader.GetString(2),
                    Nationality = nat,
                    IsInternational = isIntl,
                    NationalId = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Phone = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    CreatedAt = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.Now
                };
            }
        }

        if (student == null) return null;

        // Fetch chronological orders
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, StudentId, OrderNumber, ProgramType, Milestone, RegulationCategory, 
                       EnrollmentDate, CompletionDate, Notes, Status, Year, AcademicYear, RegulatoryTrack, SequenceNumber
                FROM TrainingOrders
                WHERE StudentId = @sid
                ORDER BY AcademicYear ASC, Year ASC, 
                         CASE WHEN EnrollmentDate IS NOT NULL THEN EnrollmentDate ELSE '9999-12-31' END ASC, 
                         Id ASC;
            ";
            cmd.Parameters.AddWithValue("@sid", studentId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var order = new TrainingOrder
                {
                    Id = reader.GetInt32(0),
                    StudentId = reader.GetInt32(1),
                    StudentDisplayName = student.DisplayName,
                    Nationality = student.Nationality,
                    OrderNumber = reader.GetString(2),
                    ProgramType = reader.GetString(3),
                    Milestone = reader.GetString(4),
                    RegulationCategory = reader.GetString(5),
                    EnrollmentDate = ParseNullableDate(reader, 6),
                    CompletionDate = ParseNullableDate(reader, 7),
                    Notes = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    Year = reader.GetInt32(10),
                    AcademicYear = reader.IsDBNull(11) ? reader.GetInt32(10) : reader.GetInt32(11),
                    RegulatoryTrack = reader.IsDBNull(12) ? "Part61" : reader.GetString(12),
                    SequenceNumber = reader.GetInt32(13)
                };

                if (order.IsActive) student.ActiveOrdersCount++;
                else student.CompletedOrdersCount++;

                student.Orders.Add(order);
            }
        }

        student.TotalOrdersCount = student.Orders.Count;
        student.HasPPL = student.Orders.Any(o => o.Milestone == "PPL");
        student.HasCPLIR = student.Orders.Any(o => o.Milestone == "CPL_IR");
        student.HasATP = student.Orders.Any(o => o.Milestone == "ATP");
        student.HasEvaluation = student.Orders.Any(o => o.Milestone == "EVALUATION");

        return student;
    }

    /// <summary>
    /// Retrieves all training orders with student metadata and multi-dimensional filtering.
    /// </summary>
    public async Task<List<TrainingOrder>> GetAllOrdersAsync(
        int? yearFilter = null, 
        string? programFilter = null, 
        string? statusFilter = null, 
        string? searchQuery = null,
        string? regulatoryTrackFilter = null,
        bool? internationalOnly = null)
    {
        var list = new List<TrainingOrder>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                o.Id, o.StudentId, s.DisplayName, s.Nationality, o.OrderNumber, o.ProgramType, o.Milestone, 
                o.RegulationCategory, o.EnrollmentDate, o.CompletionDate, o.Notes, o.Status, o.Year,
                o.AcademicYear, o.RegulatoryTrack, o.SequenceNumber
            FROM TrainingOrders o
            INNER JOIN Students s ON o.StudentId = s.Id
            WHERE (o.IsArchived = 0 OR o.IsArchived IS NULL) AND (s.IsArchived = 0 OR s.IsArchived IS NULL)
            ORDER BY o.AcademicYear DESC, o.Year DESC, o.Id DESC;
        ";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var order = new TrainingOrder
            {
                Id = reader.GetInt32(0),
                StudentId = reader.GetInt32(1),
                StudentDisplayName = reader.GetString(2),
                Nationality = reader.IsDBNull(3) ? "مصري" : reader.GetString(3),
                OrderNumber = reader.GetString(4),
                ProgramType = reader.GetString(5),
                Milestone = reader.GetString(6),
                RegulationCategory = reader.GetString(7),
                EnrollmentDate = ParseNullableDate(reader, 8),
                CompletionDate = ParseNullableDate(reader, 9),
                Notes = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                Year = reader.GetInt32(12),
                AcademicYear = reader.IsDBNull(13) ? reader.GetInt32(12) : reader.GetInt32(13),
                RegulatoryTrack = reader.IsDBNull(14) ? "Part61" : reader.GetString(14),
                SequenceNumber = reader.GetInt32(15)
            };

            // Year filter
            if (yearFilter.HasValue && yearFilter.Value > 0 && order.AcademicYear != yearFilter.Value && order.Year != yearFilter.Value)
                continue;

            // Regulatory Track filter
            if (!string.IsNullOrEmpty(regulatoryTrackFilter) && regulatoryTrackFilter != "الجميع")
            {
                if (!order.RegulatoryTrack.Equals(regulatoryTrackFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // International only filter
            if (internationalOnly.HasValue && internationalOnly.Value && !order.IsInternational)
                continue;

            // Status filter
            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "الجميع")
            {
                if (statusFilter == "قيد التدريب" && !order.IsActive)
                    continue;
                if (statusFilter == "منتهي" && order.IsActive)
                    continue;
            }

            // Milestone / Program filter
            if (!string.IsNullOrEmpty(programFilter) && programFilter != "الجميع")
            {
                if (order.Milestone != programFilter && !order.ProgramType.Contains(programFilter))
                    continue;
            }

            // Search query
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                if (!ArabicTextHelper.IsFuzzyMatch(order.StudentDisplayName, searchQuery) &&
                    !order.OrderNumber.Contains(searchQuery) &&
                    !order.Notes.Contains(searchQuery))
                {
                    continue;
                }
            }

            list.Add(order);
        }

        return list;
    }

    /// <summary>
    /// Sets or clears the completion date for an order, automatically transitioning pipeline status.
    /// </summary>
    public async Task UpdateOrderCompletionDateAsync(int orderId, DateTime? completionDate)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string status = completionDate.HasValue ? "منتهي" : "قيد التدريب";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE TrainingOrders 
            SET CompletionDate = @comp, Status = @status
            WHERE Id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.Parameters.AddWithValue("@comp", completionDate.HasValue ? completionDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", status);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Computes executive dashboard metrics scoped to an optional academic year:
    /// - Total Unique Students (Deduplicated human headcount)
    /// - Total Course Enrollments (Issued orders volume)
    /// - Active cockpit trainees vs Graduated trainees
    /// - Curriculum distribution without double-counting
    /// </summary>
    public async Task<DashboardMetrics> GetDashboardMetricsAsync(int? targetYear = null)
    {
        var metrics = new DashboardMetrics();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        bool hasYear = targetYear.HasValue && targetYear.Value > 0;
        int yr = hasYear ? targetYear!.Value : 0;

        // 1. Total Unique Students
        using (var cmd = connection.CreateCommand())
        {
            if (hasYear)
            {
                cmd.CommandText = "SELECT COUNT(DISTINCT StudentId) FROM TrainingOrders WHERE AcademicYear = @yr OR Year = @yr;";
                cmd.Parameters.AddWithValue("@yr", yr);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Students;";
            }
            metrics.TotalUniqueStudents = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // 2. Total Orders
        using (var cmd = connection.CreateCommand())
        {
            if (hasYear)
            {
                cmd.CommandText = "SELECT COUNT(*) FROM TrainingOrders WHERE AcademicYear = @yr OR Year = @yr;";
                cmd.Parameters.AddWithValue("@yr", yr);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM TrainingOrders;";
            }
            metrics.TotalCourseEnrollments = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // 3. Active Trainees
        using (var cmd = connection.CreateCommand())
        {
            if (hasYear)
            {
                cmd.CommandText = @"
                    SELECT COUNT(DISTINCT StudentId) 
                    FROM TrainingOrders 
                    WHERE (AcademicYear = @yr OR Year = @yr) AND (CompletionDate IS NULL OR CompletionDate = '');
                ";
                cmd.Parameters.AddWithValue("@yr", yr);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT COUNT(DISTINCT StudentId) 
                    FROM TrainingOrders 
                    WHERE CompletionDate IS NULL OR CompletionDate = '';
                ";
            }
            metrics.ActiveTraineesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // 4. Graduated Trainees
        using (var cmd = connection.CreateCommand())
        {
            if (hasYear)
            {
                cmd.CommandText = @"
                    SELECT COUNT(DISTINCT StudentId) 
                    FROM TrainingOrders 
                    WHERE (AcademicYear = @yr OR Year = @yr) AND (CompletionDate IS NOT NULL AND CompletionDate != '');
                ";
                cmd.Parameters.AddWithValue("@yr", yr);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT COUNT(DISTINCT s.Id)
                    FROM Students s
                    WHERE s.Id IN (SELECT DISTINCT StudentId FROM TrainingOrders)
                      AND s.Id NOT IN (
                          SELECT DISTINCT StudentId FROM TrainingOrders 
                          WHERE CompletionDate IS NULL OR CompletionDate = ''
                      );
                ";
            }
            metrics.GraduatedTraineesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // 5. Milestone & Program Distribution
        using (var cmd = connection.CreateCommand())
        {
            if (hasYear)
            {
                cmd.CommandText = @"
                    SELECT 
                        Milestone, 
                        COUNT(DISTINCT StudentId) AS UniqueStudents,
                        COUNT(Id) AS TotalOrders,
                        SUM(CASE WHEN CompletionDate IS NULL OR CompletionDate = '' THEN 1 ELSE 0 END) AS ActiveOrders
                    FROM TrainingOrders
                    WHERE AcademicYear = @yr OR Year = @yr
                    GROUP BY Milestone;
                ";
                cmd.Parameters.AddWithValue("@yr", yr);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT 
                        Milestone, 
                        COUNT(DISTINCT StudentId) AS UniqueStudents,
                        COUNT(Id) AS TotalOrders,
                        SUM(CASE WHEN CompletionDate IS NULL OR CompletionDate = '' THEN 1 ELSE 0 END) AS ActiveOrders
                    FROM TrainingOrders
                    GROUP BY Milestone;
                ";
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string milestone = reader.GetString(0);
                int uniqueStudents = reader.GetInt32(1);
                int totalOrders = reader.GetInt32(2);
                int activeOrders = reader.GetInt32(3);

                string displayName = milestone switch
                {
                    "PPL" => "طيار خاص (PPL)",
                    "CPL_IR" => "تجاري وعدادات (CPL/IR)",
                    "ATP" => "خط جوي (ATP)",
                    "EVALUATION" => "تقييم ومعادلة",
                    "TYPE_RATING" => "تجديد طراز وفرق",
                    _ => milestone
                };

                string color = milestone switch
                {
                    "PPL" => "#20C997",
                    "CPL_IR" => "#0D6EFD",
                    "ATP" => "#6610F2",
                    "EVALUATION" => "#FD7E14",
                    "TYPE_RATING" => "#0DCAF0",
                    _ => "#6C757D"
                };

                if (milestone == "PPL") metrics.PPLCount = uniqueStudents;
                else if (milestone == "CPL_IR") metrics.CPLIRCount = uniqueStudents;
                else if (milestone == "ATP") metrics.ATPCount = uniqueStudents;
                else if (milestone == "EVALUATION") metrics.EvaluationCount = uniqueStudents;
                else if (milestone == "TYPE_RATING") metrics.TypeRatingCount = uniqueStudents;

                metrics.ProgramDistribution.Add(new ProgramDistributionItem
                {
                    MilestoneKey = milestone,
                    ProgramName = displayName,
                    UniqueStudentsCount = uniqueStudents,
                    TotalOrdersCount = totalOrders,
                    ActiveCount = activeOrders,
                    Percentage = metrics.TotalUniqueStudents > 0 ? Math.Round((double)uniqueStudents / metrics.TotalUniqueStudents * 100, 1) : 0,
                    ColorHex = color
                });
            }
        }

        // 6. Recent Orders
        using (var cmd = connection.CreateCommand())
        {
            if (hasYear)
            {
                cmd.CommandText = @"
                    SELECT o.Id, o.StudentId, s.DisplayName, s.Nationality, o.OrderNumber, o.ProgramType, 
                           o.Milestone, o.RegulationCategory, o.EnrollmentDate, o.CompletionDate, o.Notes, o.Status, o.Year,
                           o.AcademicYear, o.RegulatoryTrack, o.SequenceNumber
                    FROM TrainingOrders o
                    INNER JOIN Students s ON o.StudentId = s.Id
                    WHERE o.AcademicYear = @yr OR o.Year = @yr
                    ORDER BY o.Id DESC
                    LIMIT 8;
                ";
                cmd.Parameters.AddWithValue("@yr", yr);
            }
            else
            {
                cmd.CommandText = @"
                    SELECT o.Id, o.StudentId, s.DisplayName, s.Nationality, o.OrderNumber, o.ProgramType, 
                           o.Milestone, o.RegulationCategory, o.EnrollmentDate, o.CompletionDate, o.Notes, o.Status, o.Year,
                           o.AcademicYear, o.RegulatoryTrack, o.SequenceNumber
                    FROM TrainingOrders o
                    INNER JOIN Students s ON o.StudentId = s.Id
                    ORDER BY o.Id DESC
                    LIMIT 8;
                ";
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                metrics.RecentOrders.Add(new TrainingOrder
                {
                    Id = reader.GetInt32(0),
                    StudentId = reader.GetInt32(1),
                    StudentDisplayName = reader.GetString(2),
                    Nationality = reader.IsDBNull(3) ? "مصري" : reader.GetString(3),
                    OrderNumber = reader.GetString(4),
                    ProgramType = reader.GetString(5),
                    Milestone = reader.GetString(6),
                    RegulationCategory = reader.GetString(7),
                    EnrollmentDate = ParseNullableDate(reader, 8),
                    CompletionDate = ParseNullableDate(reader, 9),
                    Notes = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    Year = reader.GetInt32(12),
                    AcademicYear = reader.IsDBNull(13) ? reader.GetInt32(12) : reader.GetInt32(13),
                    RegulatoryTrack = reader.IsDBNull(14) ? "Part61" : reader.GetString(14),
                    SequenceNumber = reader.GetInt32(15)
                });
            }
        }

        return metrics;
    }

    /// <summary>
    /// Computes full demographic and regulatory pathway metrics for an optional academic year.
    /// </summary>
    public async Task<TraineeMetricSummary> GetDemographicsSummaryAsync(int? targetYear = null)
    {
        var allStudents = await GetAllStudentsAsync(null, null, targetYear);
        var allOrders = await GetAllOrdersAsync(targetYear);
        return DemographicsEngine.ComputeMetrics(allStudents, allOrders, targetYear);
    }

    /// <summary>
    /// Standardizes the flight training pathway milestone:
    /// Unifies legacy split CPL + IR orders into the combined CPL_IR milestone.
    /// </summary>
    public static string ClassifyMilestone(string programType, string notes)
    {
        string combined = $"{programType} {notes}".ToUpperInvariant();

        if (combined.Contains("PPL") || combined.Contains("خاص"))
            return "PPL";

        // Unify CPL, IR, CPL/IR, IR/CPL, and استكمال IR/CPL under the CPL_IR milestone
        if (combined.Contains("CPL") || combined.Contains("IR") || combined.Contains("تجاري") || combined.Contains("عدادات") || combined.Contains("أهلية"))
            return "CPL_IR";

        if (combined.Contains("ATP") || combined.Contains("ATPL") || combined.Contains("خط جوي"))
            return "ATP";

        if (combined.Contains("طراز") || combined.Contains("تجديد") || combined.Contains("فرق"))
            return "TYPE_RATING";

        if (combined.Contains("تقييم") || combined.Contains("معادلة"))
            return "EVALUATION";

        return "EVALUATION";
    }

    /// <summary>
    /// Merges two student records, updating all foreign key order links and deleting the duplicate.
    /// </summary>
    public async Task MergeStudentsAsync(int targetStudentId, int sourceStudentId)
    {
        if (targetStudentId == sourceStudentId) return;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        // 1. Move all orders from source to target
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE TrainingOrders SET StudentId = @target WHERE StudentId = @source;";
            cmd.Parameters.AddWithValue("@target", targetStudentId);
            cmd.Parameters.AddWithValue("@source", sourceStudentId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Delete source student
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM Students WHERE Id = @source;";
            cmd.Parameters.AddWithValue("@source", sourceStudentId);
            await cmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task ClearAllDataAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM TrainingOrders;
            DELETE FROM Students;
            VACUUM;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Searches existing active students with live fuzzy Arabic matching for real-time deduplication.
    /// </summary>
    public async Task<List<Student>> SearchStudentsByNamePrefixAsync(string query, int maxResults = 8)
    {
        var list = new List<Student>();
        if (string.IsNullOrWhiteSpace(query)) return list;
        string norm = ArabicTextHelper.Normalize(query);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, NormalizedName, DisplayName, Nationality, IsInternational
            FROM Students
            WHERE (IsArchived = 0 OR IsArchived IS NULL) 
              AND (NormalizedName LIKE @q OR DisplayName LIKE @qRaw)
            LIMIT @limit;
        ";
        cmd.Parameters.AddWithValue("@q", $"%{norm}%");
        cmd.Parameters.AddWithValue("@qRaw", $"%{query}%");
        cmd.Parameters.AddWithValue("@limit", maxResults);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Student
            {
                Id = reader.GetInt32(0),
                NormalizedName = reader.GetString(1),
                DisplayName = reader.GetString(2),
                Nationality = reader.IsDBNull(3) ? "مصري" : reader.GetString(3),
                IsInternational = !reader.IsDBNull(4) && reader.GetInt32(4) == 1
            });
        }
        return list;
    }

    /// <summary>
    /// Creates a manual training order, automatically linking to existing student if found or creating a new canonical profile.
    /// </summary>
    public async Task<TrainingOrder> CreateManualOrderAsync(
        string studentName,
        string nationality,
        string regulatoryTrack,
        string programType,
        string orderNumber,
        DateTime enrollmentDate,
        DateTime? completionDate,
        string notes,
        int year = 0)
    {
        int studentId = await GetOrCreateStudentAsync(studentName, nationality);
        int calcYear = year > 0 ? year : (enrollmentDate.Year > 0 ? enrollmentDate.Year : DateTime.Now.Year);

        string regCat = regulatoryTrack switch
        {
            "Part61" => "61 (ج نظام حر)",
            "Part141" => "141 (ا نظام)",
            "Evaluation" => "تقييم (د)",
            "TypeRating" => "طراز وبناء ساعات",
            _ => "61 (ج نظام حر)"
        };

        var order = new TrainingOrder
        {
            StudentId = studentId,
            StudentDisplayName = studentName,
            Nationality = nationality,
            OrderNumber = orderNumber,
            ProgramType = programType,
            Milestone = ClassifyMilestone(programType, notes),
            RegulationCategory = regCat,
            EnrollmentDate = enrollmentDate,
            CompletionDate = completionDate,
            Notes = notes,
            Year = calcYear,
            AcademicYear = calcYear,
            RegulatoryTrack = regulatoryTrack,
            SequenceNumber = 0
        };

        int orderId = await InsertOrUpdateOrderAsync(order);
        order.Id = orderId;
        return order;
    }

    /// <summary>
    /// Direct status progression: transitions order to 'منتهي' and recalculates student's overall trajectory.
    /// </summary>
    public async Task<bool> CompleteOrderAsync(int orderId, DateTime completionDate, string? notes = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(notes))
        {
            cmd.CommandText = @"
                UPDATE TrainingOrders 
                SET CompletionDate = @date, Status = 'منتهي'
                WHERE Id = @id;
            ";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE TrainingOrders 
                SET CompletionDate = @date, Status = 'منتهي', Notes = CASE WHEN Notes IS NULL OR Notes = '' THEN @notes ELSE Notes || ' | ' || @notes END
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@notes", notes);
        }
        cmd.Parameters.AddWithValue("@date", completionDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@id", orderId);

        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>
    /// Soft delete (archive) order to eliminate accidental permanent deletion.
    /// </summary>
    public async Task<bool> ArchiveOrderAsync(int orderId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE TrainingOrders SET IsArchived = 1, ArchivedAt = @now WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@id", orderId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Restores a soft-deleted order from archive.
    /// </summary>
    public async Task<bool> RestoreOrderAsync(int orderId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE TrainingOrders SET IsArchived = 0, ArchivedAt = NULL WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", orderId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Retrieves all archived / soft-deleted orders for inspection and restore.
    /// </summary>
    public async Task<List<TrainingOrder>> GetArchivedOrdersAsync()
    {
        var list = new List<TrainingOrder>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT o.Id, o.StudentId, s.DisplayName, s.Nationality, o.OrderNumber, o.ProgramType,
                   o.EnrollmentDate, o.CompletionDate, o.Notes, o.Year, o.RegulatoryTrack, o.ArchivedAt
            FROM TrainingOrders o
            JOIN Students s ON o.StudentId = s.Id
            WHERE o.IsArchived = 1
            ORDER BY o.ArchivedAt DESC;
        ";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TrainingOrder
            {
                Id = reader.GetInt32(0),
                StudentId = reader.GetInt32(1),
                StudentDisplayName = reader.GetString(2),
                Nationality = reader.IsDBNull(3) ? "مصري" : reader.GetString(3),
                OrderNumber = reader.GetString(4),
                ProgramType = reader.GetString(5),
                EnrollmentDate = ParseNullableDate(reader, 6),
                CompletionDate = ParseNullableDate(reader, 7),
                Notes = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Year = reader.GetInt32(9),
                RegulatoryTrack = reader.GetString(10),
                IsArchived = true,
                ArchivedAt = ParseNullableDate(reader, 11)
            });
        }
        return list;
    }

    private static DateTime? ParseNullableDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        string val = reader.GetString(ordinal);
        if (DateTime.TryParse(val, out var dt)) return dt;
        return null;
    }
}
