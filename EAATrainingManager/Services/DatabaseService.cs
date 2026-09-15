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

            CREATE TABLE IF NOT EXISTS Part141Batches (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BatchId TEXT NOT NULL UNIQUE,
                ProgramName TEXT NOT NULL,
                StartDate TEXT,
                EndDate TEXT,
                SyllabusHours REAL NOT NULL DEFAULT 190.0,
                TrainingOrderAttachments TEXT,
                Notes TEXT,
                AcademicYear INTEGER NOT NULL DEFAULT 2026,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                ArchivedAt TEXT,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ETPBatches (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BatchId TEXT NOT NULL UNIQUE,
                RouteName TEXT NOT NULL,
                AirlineCompany TEXT,
                StartDate TEXT,
                EndDate TEXT,
                FlightHours REAL NOT NULL DEFAULT 50.0,
                Notes TEXT,
                AcademicYear INTEGER NOT NULL DEFAULT 2026,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                ArchivedAt TEXT,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_part141_batch_id ON Part141Batches(BatchId);
            CREATE INDEX IF NOT EXISTS idx_etp_batch_id ON ETPBatches(BatchId);

            CREATE TABLE IF NOT EXISTS TrainingSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                RegulatoryTrack TEXT NOT NULL DEFAULT 'Part61',
                LessonTitle TEXT NOT NULL,
                InstructorName TEXT,
                ResourceName TEXT,
                Location TEXT,
                StartAt TEXT NOT NULL,
                EndAt TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Scheduled',
                Notes TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_start ON TrainingSessions(StartAt);
            CREATE INDEX IF NOT EXISTS idx_sessions_student ON TrainingSessions(StudentId, StartAt);
            CREATE INDEX IF NOT EXISTS idx_sessions_instructor ON TrainingSessions(InstructorName, StartAt);
            CREATE INDEX IF NOT EXISTS idx_sessions_resource ON TrainingSessions(ResourceName, StartAt);

            CREATE TABLE IF NOT EXISTS AircraftResources (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Registration TEXT NOT NULL UNIQUE,
                ResourceType TEXT NOT NULL DEFAULT 'Aircraft',
                AircraftType TEXT,
                Base TEXT,
                Status TEXT NOT NULL DEFAULT 'Available',
                HobbsHours REAL NOT NULL DEFAULT 0,
                TachHours REAL NOT NULL DEFAULT 0,
                MaintenanceDueAtHours REAL,
                Notes TEXT,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_resources_status ON AircraftResources(Status);
            CREATE INDEX IF NOT EXISTS idx_resources_base ON AircraftResources(Base);

            CREATE TABLE IF NOT EXISTS AuditEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EntityType TEXT NOT NULL,
                EntityId INTEGER NOT NULL,
                Action TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Actor TEXT NOT NULL DEFAULT 'Local Operator',
                OccurredAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_audit_entity ON AuditEvents(EntityType, EntityId, OccurredAt DESC);
            CREATE INDEX IF NOT EXISTS idx_audit_time ON AuditEvents(OccurredAt DESC);

            CREATE TABLE IF NOT EXISTS ComplianceRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                RecordType TEXT NOT NULL,
                ReferenceNumber TEXT,
                IssuedAt TEXT,
                ExpiresAt TEXT,
                IsVerified INTEGER NOT NULL DEFAULT 0,
                Notes TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_compliance_student ON ComplianceRecords(StudentId, RecordType);
            CREATE INDEX IF NOT EXISTS idx_compliance_expiry ON ComplianceRecords(ExpiresAt);

            CREATE TABLE IF NOT EXISTS FlightRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TrainingSessionId INTEGER,
                StudentId INTEGER NOT NULL,
                ActivityType TEXT NOT NULL DEFAULT 'Dual',
                ResourceName TEXT,
                InstructorName TEXT,
                Route TEXT,
                StartAt TEXT NOT NULL,
                EndAt TEXT NOT NULL,
                HobbsStart REAL NOT NULL DEFAULT 0,
                HobbsEnd REAL NOT NULL DEFAULT 0,
                Landings INTEGER NOT NULL DEFAULT 0,
                Remarks TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TrainingSessionId) REFERENCES TrainingSessions(Id) ON DELETE SET NULL,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_flight_records_student ON FlightRecords(StudentId, StartAt);
            CREATE INDEX IF NOT EXISTS idx_flight_records_resource ON FlightRecords(ResourceName, StartAt);
            CREATE INDEX IF NOT EXISTS idx_flight_records_session ON FlightRecords(TrainingSessionId);

            CREATE TABLE IF NOT EXISTS TrainingAssessments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StudentId INTEGER NOT NULL,
                AssessmentType TEXT NOT NULL DEFAULT 'StageCheck',
                Title TEXT NOT NULL,
                ExaminerName TEXT,
                AttemptNumber INTEGER NOT NULL DEFAULT 1,
                Result TEXT NOT NULL DEFAULT 'Pending',
                Deficiencies TEXT,
                RemedialPlan TEXT,
                NextAction TEXT,
                AssessedAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (StudentId) REFERENCES Students(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_assessments_student ON TrainingAssessments(StudentId, AssessedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_assessments_result ON TrainingAssessments(Result, AssessedAt DESC);

            CREATE TABLE IF NOT EXISTS PersonnelRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL UNIQUE,
                Role TEXT NOT NULL DEFAULT 'Instructor',
                LicenseNumber TEXT,
                LicenseExpiresAt TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Notes TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_personnel_role ON PersonnelRecords(Role, IsActive);
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
            if (!orderCols.Contains("BatchId"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE TrainingOrders ADD COLUMN BatchId TEXT;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!orderCols.Contains("SyllabusHours"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE TrainingOrders ADD COLUMN SyllabusHours REAL NOT NULL DEFAULT 0;";
                await alterCmd.ExecuteNonQueryAsync();
            }
            if (!orderCols.Contains("TrainingOrderAttachments"))
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE TrainingOrders ADD COLUMN TrainingOrderAttachments TEXT;";
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

            // Decouple legacy airline/ETP data that was misclassified as Part141
            using var decoupleCmd = connection.CreateCommand();
            decoupleCmd.CommandText = @"
                UPDATE TrainingOrders 
                SET RegulatoryTrack = 'ETP' 
                WHERE RegulatoryTrack = 'Part141' 
                  AND (ProgramType LIKE '%خط جوي%' OR ProgramType LIKE '%ATP%' OR RegulationCategory LIKE '%خط جوي%' OR Notes LIKE '%خط جوي%');
            ";
            await decoupleCmd.ExecuteNonQueryAsync();
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
                    BatchId = @batchId,
                    SyllabusHours = @hours,
                    TrainingOrderAttachments = @attach,
                    SequenceNumber = @seq
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", existingId);
        }
        else
        {
            cmd.CommandText = @"
                INSERT INTO TrainingOrders 
                (StudentId, OrderNumber, ProgramType, Milestone, RegulationCategory, EnrollmentDate, CompletionDate, Notes, Status, Year, AcademicYear, RegulatoryTrack, BatchId, SyllabusHours, TrainingOrderAttachments, SequenceNumber)
                VALUES 
                (@sid, @ordNum, @prog, @milestone, @cat, @enroll, @complete, @notes, @status, @yr, @acadYr, @regTrack, @batchId, @hours, @attach, @seq);
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
        cmd.Parameters.AddWithValue("@batchId", (object?)order.BatchId ?? string.Empty);
        cmd.Parameters.AddWithValue("@hours", order.SyllabusHours);
        cmd.Parameters.AddWithValue("@attach", (object?)order.TrainingOrderAttachments ?? string.Empty);
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
    /// Standardizes the regulatory track (Part 61, Part 141, ETP, Evaluation, TypeRating)
    /// Decouples Part 141 approved batches from ETP / Route Flying.
    /// </summary>
    public static string ClassifyRegulatoryTrack(string category, string programType)
    {
        string combined = $"{category} {programType}".ToLowerInvariant();
        if (combined.Contains("خط جوي") || combined.Contains("etp") || combined.Contains("atp") || combined.Contains("route")) return "ETP";
        if (combined.Contains("141") || combined.Contains("معتمد") || combined.Contains("دفعة") || combined.Contains("دفعات") || combined.Contains("فرقة")) return "Part141";
        if (combined.Contains("61") || combined.Contains("حر")) return "Part61";
        if (combined.Contains("تقييم") || combined.Contains("معادلة")) return "Evaluation";
        if (combined.Contains("طراز") || combined.Contains("فرق") || combined.Contains("بناء ساعات")) return "TypeRating";
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
                    SUM(CASE WHEN o.Milestone = 'EVALUATION' THEN 1 ELSE 0 END) AS HasEval,
                    SUM(CASE WHEN o.RegulatoryTrack = 'Part141' THEN 1 ELSE 0 END) AS HasPart141,
                    SUM(CASE WHEN o.RegulatoryTrack = 'ETP' OR o.RegulatoryTrack = 'ATP' THEN 1 ELSE 0 END) AS HasETP
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
                    SUM(CASE WHEN o.Milestone = 'EVALUATION' THEN 1 ELSE 0 END) AS HasEval,
                    SUM(CASE WHEN o.RegulatoryTrack = 'Part141' THEN 1 ELSE 0 END) AS HasPart141,
                    SUM(CASE WHEN o.RegulatoryTrack = 'ETP' OR o.RegulatoryTrack = 'ATP' THEN 1 ELSE 0 END) AS HasETP
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
                HasEvaluation = reader.GetInt32(14) > 0,
                HasPart141 = reader.GetInt32(15) > 0,
                HasETP = reader.GetInt32(16) > 0
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
                o.AcademicYear, o.RegulatoryTrack, o.SequenceNumber,
                o.BatchId, o.SyllabusHours, o.TrainingOrderAttachments
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
                SequenceNumber = reader.GetInt32(15),
                BatchId = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                SyllabusHours = reader.IsDBNull(17) ? 0.0 : reader.GetDouble(17),
                TrainingOrderAttachments = reader.IsDBNull(18) ? string.Empty : reader.GetString(18)
            };

            // Year filter
            if (yearFilter.HasValue && yearFilter.Value > 0 && order.AcademicYear != yearFilter.Value && order.Year != yearFilter.Value)
                continue;

            // Regulatory Track filter
            if (!string.IsNullOrEmpty(regulatoryTrackFilter) && regulatoryTrackFilter != "الجميع")
            {
                if (regulatoryTrackFilter.Equals("ETP", StringComparison.OrdinalIgnoreCase) || regulatoryTrackFilter.Equals("ATP", StringComparison.OrdinalIgnoreCase))
                {
                    if (!order.RegulatoryTrack.Equals("ETP", StringComparison.OrdinalIgnoreCase) && !order.RegulatoryTrack.Equals("ATP", StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else
                {
                    if (!order.RegulatoryTrack.Equals(regulatoryTrackFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
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
            DELETE FROM AuditEvents;
            DELETE FROM PersonnelRecords;
            DELETE FROM TrainingAssessments;
            DELETE FROM FlightRecords;
            DELETE FROM ComplianceRecords;
            DELETE FROM TrainingSessions;
            DELETE FROM AircraftResources;
            DELETE FROM TrainingOrders;
            DELETE FROM Students;
            VACUUM;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Returns the operational board for a calendar day, including student names.
    /// </summary>
    public async Task<List<TrainingSession>> GetTrainingSessionsAsync(DateTime date)
    {
        var sessions = new List<TrainingSession>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ts.Id, ts.StudentId, s.DisplayName, ts.RegulatoryTrack, ts.LessonTitle,
                   ts.InstructorName, ts.ResourceName, ts.Location, ts.StartAt, ts.EndAt,
                   ts.Status, ts.Notes, ts.CreatedAt
            FROM TrainingSessions ts
            INNER JOIN Students s ON s.Id = ts.StudentId
            WHERE ts.StartAt >= @start AND ts.StartAt < @end
            ORDER BY ts.StartAt, ts.InstructorName, ts.ResourceName;
        ";
        cmd.Parameters.AddWithValue("@start", date.Date.ToString("o"));
        cmd.Parameters.AddWithValue("@end", date.Date.AddDays(1).ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new TrainingSession
            {
                Id = reader.GetInt32(0),
                StudentId = reader.GetInt32(1),
                StudentDisplayName = reader.GetString(2),
                RegulatoryTrack = reader.IsDBNull(3) ? "Part61" : reader.GetString(3),
                LessonTitle = reader.GetString(4),
                InstructorName = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                ResourceName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Location = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                StartAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
                EndAt = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                Status = reader.IsDBNull(10) ? "Scheduled" : reader.GetString(10),
                Notes = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                CreatedAt = DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture)
            });
        }
        return sessions;
    }

    /// <summary>
    /// Schedules a training activity and refuses overlapping student, instructor, or resource allocations.
    /// </summary>
    public async Task<int> ScheduleTrainingSessionAsync(TrainingSession session)
    {
        if (session.StudentId <= 0) throw new ArgumentException("A student is required for scheduling.", nameof(session));
        if (string.IsNullOrWhiteSpace(session.LessonTitle)) throw new ArgumentException("A lesson title is required.", nameof(session));
        if (session.EndAt <= session.StartAt) throw new ArgumentException("The session end time must be after its start time.", nameof(session));
        if (await HasExpiredVerifiedComplianceAsync(session.StudentId))
            throw new InvalidOperationException("The trainee has an expired verified compliance record and cannot be scheduled.");
        if (!await IsResourceDispatchableAsync(session.ResourceName))
            throw new InvalidOperationException($"Resource '{session.ResourceName}' is unavailable or maintenance-due.");

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using (var conflictCmd = connection.CreateCommand())
        {
            conflictCmd.CommandText = @"
                SELECT s.DisplayName, ts.LessonTitle, ts.StartAt, ts.EndAt
                FROM TrainingSessions ts
                INNER JOIN Students s ON s.Id = ts.StudentId
                WHERE ts.Status <> 'Cancelled'
                  AND ts.StartAt < @end AND ts.EndAt > @start
                  AND (
                    ts.StudentId = @studentId
                    OR (@instructor <> '' AND ts.InstructorName = @instructor)
                    OR (@resource <> '' AND ts.ResourceName = @resource)
                  )
                LIMIT 1;
            ";
            conflictCmd.Parameters.AddWithValue("@start", session.StartAt.ToString("o"));
            conflictCmd.Parameters.AddWithValue("@end", session.EndAt.ToString("o"));
            conflictCmd.Parameters.AddWithValue("@studentId", session.StudentId);
            conflictCmd.Parameters.AddWithValue("@instructor", session.InstructorName.Trim());
            conflictCmd.Parameters.AddWithValue("@resource", session.ResourceName.Trim());

            using var conflictReader = await conflictCmd.ExecuteReaderAsync();
            if (await conflictReader.ReadAsync())
            {
                string name = conflictReader.GetString(0);
                string lesson = conflictReader.GetString(1);
                throw new InvalidOperationException($"Scheduling conflict with {name}: {lesson}.");
            }
        }

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO TrainingSessions
            (StudentId, RegulatoryTrack, LessonTitle, InstructorName, ResourceName, Location, StartAt, EndAt, Status, Notes, CreatedAt)
            VALUES
            (@studentId, @track, @lesson, @instructor, @resource, @location, @start, @end, @status, @notes, @created);
            SELECT last_insert_rowid();
        ";
        insertCmd.Parameters.AddWithValue("@studentId", session.StudentId);
        insertCmd.Parameters.AddWithValue("@track", session.RegulatoryTrack);
        insertCmd.Parameters.AddWithValue("@lesson", session.LessonTitle.Trim());
        insertCmd.Parameters.AddWithValue("@instructor", session.InstructorName.Trim());
        insertCmd.Parameters.AddWithValue("@resource", session.ResourceName.Trim());
        insertCmd.Parameters.AddWithValue("@location", session.Location.Trim());
        insertCmd.Parameters.AddWithValue("@start", session.StartAt.ToString("o"));
        insertCmd.Parameters.AddWithValue("@end", session.EndAt.ToString("o"));
        insertCmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(session.Status) ? "Scheduled" : session.Status);
        insertCmd.Parameters.AddWithValue("@notes", session.Notes.Trim());
        insertCmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        int sessionId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
        await RecordAuditEventAsync("TrainingSession", sessionId, "Created", $"Scheduled {session.LessonTitle.Trim()} for student #{session.StudentId}.");
        return sessionId;
    }

    public async Task<bool> UpdateTrainingSessionStatusAsync(int sessionId, string status)
    {
        if (sessionId <= 0) return false;
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE TrainingSessions SET Status = @status WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@id", sessionId);
        bool updated = await cmd.ExecuteNonQueryAsync() > 0;
        if (updated)
            await RecordAuditEventAsync("TrainingSession", sessionId, "StatusChanged", $"Session status changed to {status}");
        return updated;
    }

    /// <summary>
    /// Records completed flight, simulator, or ground-training evidence. Aircraft Hobbs time and the linked
    /// operational session are updated in the same transaction so the daily board and utilization stay aligned.
    /// </summary>
    public async Task<int> RecordFlightAsync(FlightRecord record)
    {
        if (record.StudentId <= 0) throw new ArgumentException("A trainee is required for a flight record.", nameof(record));
        if (record.EndAt <= record.StartAt) throw new ArgumentException("The activity end time must be after its start time.", nameof(record));
        if (record.HobbsEnd < record.HobbsStart) throw new ArgumentException("Hobbs end must not be less than Hobbs start.", nameof(record));
        if (record.Landings < 0) throw new ArgumentException("Landings cannot be negative.", nameof(record));

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        int recordId;
        try
        {
            using (var insertCmd = connection.CreateCommand())
            {
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO FlightRecords
                    (TrainingSessionId, StudentId, ActivityType, ResourceName, InstructorName, Route, StartAt, EndAt, HobbsStart, HobbsEnd, Landings, Remarks, CreatedAt)
                    VALUES
                    (@sessionId, @studentId, @activityType, @resource, @instructor, @route, @start, @end, @hobbsStart, @hobbsEnd, @landings, @remarks, @created);
                    SELECT last_insert_rowid();
                ";
                insertCmd.Parameters.AddWithValue("@sessionId", record.TrainingSessionId.HasValue ? record.TrainingSessionId.Value : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@studentId", record.StudentId);
                insertCmd.Parameters.AddWithValue("@activityType", string.IsNullOrWhiteSpace(record.ActivityType) ? "Dual" : record.ActivityType.Trim());
                insertCmd.Parameters.AddWithValue("@resource", record.ResourceName.Trim());
                insertCmd.Parameters.AddWithValue("@instructor", record.InstructorName.Trim());
                insertCmd.Parameters.AddWithValue("@route", record.Route.Trim());
                insertCmd.Parameters.AddWithValue("@start", record.StartAt.ToString("o"));
                insertCmd.Parameters.AddWithValue("@end", record.EndAt.ToString("o"));
                insertCmd.Parameters.AddWithValue("@hobbsStart", record.HobbsStart);
                insertCmd.Parameters.AddWithValue("@hobbsEnd", record.HobbsEnd);
                insertCmd.Parameters.AddWithValue("@landings", record.Landings);
                insertCmd.Parameters.AddWithValue("@remarks", record.Remarks.Trim());
                insertCmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
                recordId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
            }

            if (!string.IsNullOrWhiteSpace(record.ResourceName))
            {
                using var resourceCmd = connection.CreateCommand();
                resourceCmd.Transaction = transaction;
                resourceCmd.CommandText = @"
                    UPDATE AircraftResources
                    SET HobbsHours = CASE WHEN HobbsHours < @hobbsEnd THEN @hobbsEnd ELSE HobbsHours END
                    WHERE Registration = @registration;
                ";
                resourceCmd.Parameters.AddWithValue("@hobbsEnd", record.HobbsEnd);
                resourceCmd.Parameters.AddWithValue("@registration", record.ResourceName.Trim());
                await resourceCmd.ExecuteNonQueryAsync();
            }

            if (record.TrainingSessionId.HasValue)
            {
                using var sessionCmd = connection.CreateCommand();
                sessionCmd.Transaction = transaction;
                sessionCmd.CommandText = "UPDATE TrainingSessions SET Status = 'Completed' WHERE Id = @id;";
                sessionCmd.Parameters.AddWithValue("@id", record.TrainingSessionId.Value);
                await sessionCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        await RecordAuditEventAsync("FlightRecord", recordId, "Created", $"Recorded {record.ActivityType} activity for student #{record.StudentId}.");
        if (record.TrainingSessionId.HasValue)
            await RecordAuditEventAsync("TrainingSession", record.TrainingSessionId.Value, "CompletedFromRecord", $"Completed from flight record #{recordId}.");
        return recordId;
    }

    public async Task<List<FlightRecord>> GetFlightRecordsAsync(DateTime? date = null, int? studentId = null)
    {
        var records = new List<FlightRecord>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT fr.Id, fr.TrainingSessionId, fr.StudentId, s.DisplayName, fr.ActivityType, fr.ResourceName,
                   fr.InstructorName, fr.Route, fr.StartAt, fr.EndAt, fr.HobbsStart, fr.HobbsEnd, fr.Landings,
                   fr.Remarks, fr.CreatedAt
            FROM FlightRecords fr
            INNER JOIN Students s ON s.Id = fr.StudentId
            WHERE (@studentId IS NULL OR fr.StudentId = @studentId)
              AND (@start IS NULL OR (fr.StartAt >= @start AND fr.StartAt < @end))
            ORDER BY fr.StartAt DESC, fr.Id DESC;
        ";
        cmd.Parameters.AddWithValue("@studentId", studentId.HasValue ? studentId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@start", date.HasValue ? date.Value.Date.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@end", date.HasValue ? date.Value.Date.AddDays(1).ToString("o") : DBNull.Value);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new FlightRecord
            {
                Id = reader.GetInt32(0),
                TrainingSessionId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                StudentId = reader.GetInt32(2),
                StudentDisplayName = reader.GetString(3),
                ActivityType = reader.IsDBNull(4) ? "Dual" : reader.GetString(4),
                ResourceName = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                InstructorName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Route = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                StartAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
                EndAt = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                HobbsStart = reader.IsDBNull(10) ? 0 : reader.GetDouble(10),
                HobbsEnd = reader.IsDBNull(11) ? 0 : reader.GetDouble(11),
                Landings = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                Remarks = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                CreatedAt = DateTime.Parse(reader.GetString(14), CultureInfo.InvariantCulture)
            });
        }
        return records;
    }

    public async Task<int> SaveTrainingAssessmentAsync(TrainingAssessment assessment)
    {
        if (assessment.StudentId <= 0) throw new ArgumentException("A trainee is required.", nameof(assessment));
        if (string.IsNullOrWhiteSpace(assessment.Title)) throw new ArgumentException("An assessment title is required.", nameof(assessment));
        if (assessment.AttemptNumber < 1) throw new ArgumentException("Attempt number must be at least one.", nameof(assessment));
        string[] allowedResults = ["Pending", "Passed", "Failed", "Conditional"];
        if (!allowedResults.Contains(assessment.Result, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Assessment result is invalid.", nameof(assessment));
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO TrainingAssessments (StudentId, AssessmentType, Title, ExaminerName, AttemptNumber, Result, Deficiencies, RemedialPlan, NextAction, AssessedAt, CreatedAt)
            VALUES (@studentId, @type, @title, @examiner, @attempt, @result, @deficiencies, @remedial, @nextAction, @assessedAt, @createdAt);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@studentId", assessment.StudentId); cmd.Parameters.AddWithValue("@type", assessment.AssessmentType.Trim()); cmd.Parameters.AddWithValue("@title", assessment.Title.Trim()); cmd.Parameters.AddWithValue("@examiner", assessment.ExaminerName.Trim()); cmd.Parameters.AddWithValue("@attempt", assessment.AttemptNumber); cmd.Parameters.AddWithValue("@result", assessment.Result); cmd.Parameters.AddWithValue("@deficiencies", assessment.Deficiencies.Trim()); cmd.Parameters.AddWithValue("@remedial", assessment.RemedialPlan.Trim()); cmd.Parameters.AddWithValue("@nextAction", assessment.NextAction.Trim()); cmd.Parameters.AddWithValue("@assessedAt", assessment.AssessedAt.ToString("o")); cmd.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("o"));
        int id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        await RecordAuditEventAsync("TrainingAssessment", id, "Created", $"Recorded {assessment.Result} assessment for student #{assessment.StudentId}.");
        return id;
    }

    public async Task<List<TrainingAssessment>> GetTrainingAssessmentsAsync(int? studentId = null)
    {
        var assessments = new List<TrainingAssessment>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(); using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT a.Id, a.StudentId, s.DisplayName, a.AssessmentType, a.Title, a.ExaminerName, a.AttemptNumber, a.Result, a.Deficiencies, a.RemedialPlan, a.NextAction, a.AssessedAt, a.CreatedAt FROM TrainingAssessments a INNER JOIN Students s ON s.Id = a.StudentId WHERE (@studentId IS NULL OR a.StudentId = @studentId) ORDER BY a.AssessedAt DESC, a.Id DESC;";
        cmd.Parameters.AddWithValue("@studentId", studentId.HasValue ? studentId.Value : DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) assessments.Add(new TrainingAssessment { Id = r.GetInt32(0), StudentId = r.GetInt32(1), StudentDisplayName = r.GetString(2), AssessmentType = r.GetString(3), Title = r.GetString(4), ExaminerName = r.IsDBNull(5) ? string.Empty : r.GetString(5), AttemptNumber = r.GetInt32(6), Result = r.GetString(7), Deficiencies = r.IsDBNull(8) ? string.Empty : r.GetString(8), RemedialPlan = r.IsDBNull(9) ? string.Empty : r.GetString(9), NextAction = r.IsDBNull(10) ? string.Empty : r.GetString(10), AssessedAt = DateTime.Parse(r.GetString(11), CultureInfo.InvariantCulture), CreatedAt = DateTime.Parse(r.GetString(12), CultureInfo.InvariantCulture) });
        return assessments;
    }

    public async Task<int> SavePersonnelAsync(PersonnelRecord person)
    {
        if (string.IsNullOrWhiteSpace(person.FullName)) throw new ArgumentException("A personnel name is required.", nameof(person));
        using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(); using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO PersonnelRecords (FullName, Role, LicenseNumber, LicenseExpiresAt, IsActive, Notes, CreatedAt) VALUES (@name,@role,@license,@expires,@active,@notes,@created) ON CONFLICT(FullName) DO UPDATE SET Role=excluded.Role, LicenseNumber=excluded.LicenseNumber, LicenseExpiresAt=excluded.LicenseExpiresAt, IsActive=excluded.IsActive, Notes=excluded.Notes; SELECT Id FROM PersonnelRecords WHERE FullName=@name;";
        cmd.Parameters.AddWithValue("@name", person.FullName.Trim()); cmd.Parameters.AddWithValue("@role", person.Role.Trim()); cmd.Parameters.AddWithValue("@license", person.LicenseNumber.Trim()); cmd.Parameters.AddWithValue("@expires", person.LicenseExpiresAt.HasValue ? person.LicenseExpiresAt.Value.ToString("o") : DBNull.Value); cmd.Parameters.AddWithValue("@active", person.IsActive ? 1 : 0); cmd.Parameters.AddWithValue("@notes", person.Notes.Trim()); cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        int id=Convert.ToInt32(await cmd.ExecuteScalarAsync()); await RecordAuditEventAsync("Personnel",id,"Saved",$"Saved {person.Role} {person.FullName.Trim()}."); return id;
    }

    public async Task<List<PersonnelRecord>> GetPersonnelAsync(bool currentOnly = false)
    {
        var people=new List<PersonnelRecord>(); using var connection=new SqliteConnection(_connectionString); await connection.OpenAsync(); using var cmd=connection.CreateCommand();
        cmd.CommandText=@"SELECT Id,FullName,Role,LicenseNumber,LicenseExpiresAt,IsActive,Notes FROM PersonnelRecords WHERE (@currentOnly=0 OR (IsActive=1 AND (LicenseExpiresAt IS NULL OR LicenseExpiresAt>=@today))) ORDER BY FullName;"; cmd.Parameters.AddWithValue("@currentOnly",currentOnly?1:0);cmd.Parameters.AddWithValue("@today",DateTime.Today.ToString("o")); using var r=await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) people.Add(new PersonnelRecord{Id=r.GetInt32(0),FullName=r.GetString(1),Role=r.GetString(2),LicenseNumber=r.IsDBNull(3)?string.Empty:r.GetString(3),LicenseExpiresAt=r.IsDBNull(4)?null:DateTime.Parse(r.GetString(4),CultureInfo.InvariantCulture),IsActive=r.GetInt32(5)==1,Notes=r.IsDBNull(6)?string.Empty:r.GetString(6)}); return people;
    }

    public async Task RecordAuditEventAsync(string entityType, int entityId, string action, string summary, string actor = "Local Operator")
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AuditEvents (EntityType, EntityId, Action, Summary, Actor, OccurredAt)
            VALUES (@entityType, @entityId, @action, @summary, @actor, @occurredAt);
        ";
        cmd.Parameters.AddWithValue("@entityType", entityType);
        cmd.Parameters.AddWithValue("@entityId", entityId);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@summary", summary);
        cmd.Parameters.AddWithValue("@actor", actor);
        cmd.Parameters.AddWithValue("@occurredAt", DateTime.Now.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AuditEvent>> GetAuditEventsAsync(string? entityType = null, int? entityId = null, int limit = 200)
    {
        var events = new List<AuditEvent>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, EntityType, EntityId, Action, Summary, Actor, OccurredAt
            FROM AuditEvents
            WHERE (@entityType IS NULL OR EntityType = @entityType)
              AND (@entityId IS NULL OR EntityId = @entityId)
            ORDER BY OccurredAt DESC
            LIMIT @limit;
        ";
        cmd.Parameters.AddWithValue("@entityType", string.IsNullOrWhiteSpace(entityType) ? DBNull.Value : entityType);
        cmd.Parameters.AddWithValue("@entityId", entityId.HasValue ? entityId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 1000));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new AuditEvent
            {
                Id = reader.GetInt32(0),
                EntityType = reader.GetString(1),
                EntityId = reader.GetInt32(2),
                Action = reader.GetString(3),
                Summary = reader.GetString(4),
                Actor = reader.GetString(5),
                OccurredAt = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture)
            });
        }
        return events;
    }

    public async Task<List<ComplianceRecord>> GetComplianceRecordsAsync(DateTime? expiringBefore = null)
    {
        var records = new List<ComplianceRecord>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id, c.StudentId, s.DisplayName, c.RecordType, c.ReferenceNumber,
                   c.IssuedAt, c.ExpiresAt, c.IsVerified, c.Notes, c.CreatedAt
            FROM ComplianceRecords c
            INNER JOIN Students s ON s.Id = c.StudentId
            WHERE (@before IS NULL OR (c.ExpiresAt IS NOT NULL AND c.ExpiresAt <= @before))
            ORDER BY CASE WHEN c.ExpiresAt IS NULL THEN 1 ELSE 0 END, c.ExpiresAt, s.DisplayName;
        ";
        cmd.Parameters.AddWithValue("@before", expiringBefore.HasValue ? expiringBefore.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new ComplianceRecord
            {
                Id = reader.GetInt32(0),
                StudentId = reader.GetInt32(1),
                StudentDisplayName = reader.GetString(2),
                RecordType = reader.GetString(3),
                ReferenceNumber = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                IssuedAt = ParseNullableDate(reader, 5),
                ExpiresAt = ParseNullableDate(reader, 6),
                IsVerified = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,
                Notes = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                CreatedAt = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture)
            });
        }
        return records;
    }

    public async Task<int> SaveComplianceRecordAsync(ComplianceRecord record)
    {
        if (record.StudentId <= 0) throw new ArgumentException("A student is required.", nameof(record));
        if (record.ExpiresAt.HasValue && record.IssuedAt.HasValue && record.ExpiresAt < record.IssuedAt)
            throw new ArgumentException("The expiry date cannot be before the issue date.", nameof(record));
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ComplianceRecords
            (StudentId, RecordType, ReferenceNumber, IssuedAt, ExpiresAt, IsVerified, Notes, CreatedAt)
            VALUES (@studentId, @type, @reference, @issued, @expires, @verified, @notes, @created);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("@studentId", record.StudentId);
        cmd.Parameters.AddWithValue("@type", record.RecordType);
        cmd.Parameters.AddWithValue("@reference", record.ReferenceNumber.Trim());
        cmd.Parameters.AddWithValue("@issued", record.IssuedAt.HasValue ? record.IssuedAt.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("@expires", record.ExpiresAt.HasValue ? record.ExpiresAt.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("@verified", record.IsVerified ? 1 : 0);
        cmd.Parameters.AddWithValue("@notes", record.Notes.Trim());
        cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        int id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        await RecordAuditEventAsync("ComplianceRecord", id, "Created", $"Added {record.RecordType} record for student #{record.StudentId}.");
        return id;
    }

    public async Task<bool> HasExpiredVerifiedComplianceAsync(int studentId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM ComplianceRecords
            WHERE StudentId = @studentId AND IsVerified = 1
              AND ExpiresAt IS NOT NULL AND ExpiresAt < @today;
        ";
        cmd.Parameters.AddWithValue("@studentId", studentId);
        cmd.Parameters.AddWithValue("@today", DateTime.Today.ToString("yyyy-MM-dd"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<List<AircraftResource>> GetAircraftResourcesAsync()
    {
        var resources = new List<AircraftResource>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Registration, ResourceType, AircraftType, Base, Status,
                   HobbsHours, TachHours, MaintenanceDueAtHours, Notes, CreatedAt
            FROM AircraftResources
            ORDER BY CASE WHEN Status = 'Available' THEN 0 ELSE 1 END, Registration;
        ";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            resources.Add(new AircraftResource
            {
                Id = reader.GetInt32(0),
                Registration = reader.GetString(1),
                ResourceType = reader.IsDBNull(2) ? "Aircraft" : reader.GetString(2),
                AircraftType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Base = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Status = reader.IsDBNull(5) ? "Available" : reader.GetString(5),
                HobbsHours = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                TachHours = reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                MaintenanceDueAtHours = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                Notes = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                CreatedAt = DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture)
            });
        }
        return resources;
    }

    public async Task<int> SaveAircraftResourceAsync(AircraftResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Registration))
            throw new ArgumentException("A registration or simulator identifier is required.", nameof(resource));
        if (resource.HobbsHours < 0 || resource.TachHours < 0)
            throw new ArgumentException("Meter hours cannot be negative.", nameof(resource));

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AircraftResources
            (Registration, ResourceType, AircraftType, Base, Status, HobbsHours, TachHours, MaintenanceDueAtHours, Notes, CreatedAt)
            VALUES
            (@registration, @resourceType, @aircraftType, @base, @status, @hobbs, @tach, @due, @notes, @created)
            ON CONFLICT(Registration) DO UPDATE SET
                ResourceType = excluded.ResourceType,
                AircraftType = excluded.AircraftType,
                Base = excluded.Base,
                Status = excluded.Status,
                HobbsHours = excluded.HobbsHours,
                TachHours = excluded.TachHours,
                MaintenanceDueAtHours = excluded.MaintenanceDueAtHours,
                Notes = excluded.Notes;
            SELECT Id FROM AircraftResources WHERE Registration = @registration;
        ";
        cmd.Parameters.AddWithValue("@registration", resource.Registration.Trim());
        cmd.Parameters.AddWithValue("@resourceType", resource.ResourceType);
        cmd.Parameters.AddWithValue("@aircraftType", resource.AircraftType.Trim());
        cmd.Parameters.AddWithValue("@base", resource.Base.Trim());
        cmd.Parameters.AddWithValue("@status", resource.Status);
        cmd.Parameters.AddWithValue("@hobbs", resource.HobbsHours);
        cmd.Parameters.AddWithValue("@tach", resource.TachHours);
        cmd.Parameters.AddWithValue("@due", resource.MaintenanceDueAtHours.HasValue ? resource.MaintenanceDueAtHours.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", resource.Notes.Trim());
        cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        int resourceId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        await RecordAuditEventAsync("AircraftResource", resourceId, "Saved", $"Saved resource {resource.Registration.Trim()} with status {resource.Status}.");
        return resourceId;
    }

    public async Task<bool> IsResourceDispatchableAsync(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName)) return true;
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Status, HobbsHours, MaintenanceDueAtHours
            FROM AircraftResources
            WHERE Registration = @resource
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("@resource", resourceName.Trim());
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return true;
        string status = reader.IsDBNull(0) ? "Available" : reader.GetString(0);
        double hobbs = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
        double? due = reader.IsDBNull(2) ? null : reader.GetDouble(2);
        return status == "Available" && (!due.HasValue || hobbs < due.Value);
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
        int year = 0,
        string batchId = "",
        double syllabusHours = 0.0,
        string attachments = "")
    {
        int studentId = await GetOrCreateStudentAsync(studentName, nationality);
        int calcYear = year > 0 ? year : (enrollmentDate.Year > 0 ? enrollmentDate.Year : DateTime.Now.Year);

        string regCat = regulatoryTrack switch
        {
            "Part61" => "61 (ج نظام حر)",
            "Part141" => "141 (ا نظام)",
            "ETP" => "خط جوي (ETP)",
            "ATP" => "خط جوي (ETP)",
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
            BatchId = batchId,
            SyllabusHours = syllabusHours,
            TrainingOrderAttachments = attachments,
            SequenceNumber = 0
        };

        int orderId = await InsertOrUpdateOrderAsync(order);
        order.Id = orderId;

        // Materialize the dedicated module record as part of order creation.
        if (string.Equals(regulatoryTrack, "Part141", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(batchId))
        {
            string programName = programType;
            int separator = programName.IndexOf(" - ", StringComparison.OrdinalIgnoreCase);
            if (separator > 0) programName = programName[..separator].Trim();

            await CreateOrUpdatePart141BatchAsync(new Part141Batch
            {
                BatchId = batchId.Trim(),
                ProgramName = string.IsNullOrWhiteSpace(programName) ? "Part 141" : programName,
                StartDate = enrollmentDate,
                EndDate = completionDate,
                SyllabusHours = syllabusHours > 0 ? syllabusHours : 190,
                TrainingOrderAttachments = attachments,
                AcademicYear = calcYear,
                Notes = notes
            });
        }
        else if ((string.Equals(regulatoryTrack, "ETP", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(regulatoryTrack, "ATP", StringComparison.OrdinalIgnoreCase)) &&
                 !string.IsNullOrWhiteSpace(batchId))
        {
            string routeName = programType;
            int open = routeName.IndexOf('(');
            int close = routeName.LastIndexOf(')');
            if (open >= 0 && close > open) routeName = routeName[(open + 1)..close].Trim();

            string airline = string.Empty;
            int operatorIndex = notes.IndexOf("Ù…Ø´ØºÙ„:", StringComparison.OrdinalIgnoreCase);
            if (operatorIndex >= 0)
            {
                string value = notes[(operatorIndex + 8)..];
                int pipe = value.IndexOf('|');
                airline = (pipe >= 0 ? value[..pipe] : value).Trim();
            }

            await CreateOrUpdateETPBatchAsync(new ETPBatch
            {
                BatchId = batchId.Trim(),
                RouteName = string.IsNullOrWhiteSpace(routeName) ? batchId.Trim() : routeName,
                AirlineCompany = airline,
                StartDate = enrollmentDate,
                EndDate = completionDate,
                FlightHours = syllabusHours > 0 ? syllabusHours : 50,
                AcademicYear = calcYear,
                Notes = notes
            });
        }

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

    /// <summary>
    /// Retrieves Part 141 approved batches with student counts, progress aggregations,
    /// nationality breakdown (local vs foreign), and student roster drilldown.
    /// Also infers batches from historical Part 141 orders if not yet explicitly in Part141Batches table.
    /// </summary>
    public async Task<List<Part141Batch>> GetPart141BatchesAsync(int? academicYear = null, string? searchQuery = null)
    {
        var batches = new List<Part141Batch>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 1. Fetch persisted batches from Part141Batches
        using (var cmd = connection.CreateCommand())
        {
            string sql = "SELECT Id, BatchId, ProgramName, StartDate, EndDate, SyllabusHours, TrainingOrderAttachments, Notes, AcademicYear, IsArchived, CreatedAt FROM Part141Batches WHERE (IsArchived = 0 OR IsArchived IS NULL)";
            if (academicYear.HasValue && academicYear.Value > 0)
            {
                sql += " AND AcademicYear = @yr";
                cmd.Parameters.AddWithValue("@yr", academicYear.Value);
            }
            sql += " ORDER BY BatchId DESC, Id DESC;";
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                batches.Add(new Part141Batch
                {
                    Id = reader.GetInt32(0),
                    BatchId = reader.GetString(1),
                    ProgramName = reader.GetString(2),
                    StartDate = ParseNullableDate(reader, 3),
                    EndDate = ParseNullableDate(reader, 4),
                    SyllabusHours = reader.IsDBNull(5) ? 190.0 : reader.GetDouble(5),
                    TrainingOrderAttachments = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Notes = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    AcademicYear = reader.IsDBNull(8) ? 2026 : reader.GetInt32(8),
                    IsArchived = (reader.IsDBNull(9) ? 0 : reader.GetInt32(9)) == 1,
                    CreatedAt = DateTime.TryParse(reader.GetString(10), out var dt) ? dt : DateTime.Now
                });
            }
        }

        // 2. Fetch all Part 141 orders to aggregate student counts, nationality breakdown, and student rosters
        // STRICT DECOUPLING: Only pull orders where RegulatoryTrack = 'Part141' (no ETP/airline data!)
        var p141Orders = await GetAllOrdersAsync(yearFilter: academicYear, regulatoryTrackFilter: "Part141");
        
        // Group orders by BatchId (or inferred from ProgramType/Notes)
        var ordersByBatch = new Dictionary<string, List<TrainingOrder>>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in p141Orders)
        {
            string batchKey = !string.IsNullOrWhiteSpace(order.BatchId) ? order.BatchId.Trim() : ExtractBatchId(order.ProgramType, order.Notes);
            if (string.IsNullOrWhiteSpace(batchKey)) batchKey = "عام";

            if (!ordersByBatch.TryGetValue(batchKey, out var list))
            {
                list = new List<TrainingOrder>();
                ordersByBatch[batchKey] = list;
            }
            list.Add(order);
        }

        // 3. For any batches that were inferred from orders but not yet in Part141Batches table, add them
        foreach (var kvp in ordersByBatch)
        {
            if (!batches.Any(b => b.BatchId.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)))
            {
                var sampleOrder = kvp.Value.FirstOrDefault();
                batches.Add(new Part141Batch
                {
                    Id = 0,
                    BatchId = kvp.Key,
                    ProgramName = sampleOrder?.ProgramType ?? $"دفعة {kvp.Key}",
                    StartDate = kvp.Value.Where(o => o.EnrollmentDate.HasValue).Min(o => o.EnrollmentDate),
                    EndDate = kvp.Value.Where(o => o.CompletionDate.HasValue).Max(o => o.CompletionDate),
                    SyllabusHours = 190.0,
                    AcademicYear = sampleOrder?.AcademicYear ?? (academicYear ?? 2026),
                    Notes = "تم التجميع تلقائياً من أوامر التدريب المعتمدة"
                });
            }
        }

        // 4. Fetch all students to build rosters
        var allStudents = await GetAllStudentsAsync(academicYear: academicYear);
        var studentDict = allStudents.ToDictionary(s => s.Id);

        foreach (var batch in batches)
        {
            if (ordersByBatch.TryGetValue(batch.BatchId, out var bOrders))
            {
                var uniqueStudentIds = bOrders.Select(o => o.StudentId).Distinct().ToList();
                var roster = uniqueStudentIds.Where(id => studentDict.ContainsKey(id)).Select(id => studentDict[id]).ToList();

                batch.TotalStudents = roster.Count;
                batch.GraduatedStudents = bOrders.Where(o => !o.IsActive).Select(o => o.StudentId).Distinct().Count();
                batch.LocalStudentsCount = roster.Count(s => !s.IsInternational);
                batch.InternationalStudentsCount = roster.Count(s => s.IsInternational);

                // Group by nationality
                batch.NationalityBreakdown = roster
                    .GroupBy(s => DemographicsEngine.StandardizeNationality(s.Nationality))
                    .Select(g => new NationalityCountItem
                    {
                        Country = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(n => n.Count)
                    .ToList();

                batch.StudentRoster = roster;
            }
        }

        // Search filtering if specified
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            batches = batches.Where(b => 
                ArabicTextHelper.IsFuzzyMatch(b.BatchId, searchQuery) ||
                ArabicTextHelper.IsFuzzyMatch(b.ProgramName, searchQuery) ||
                ArabicTextHelper.IsFuzzyMatch(b.Notes, searchQuery) ||
                b.StudentRoster.Any(s => ArabicTextHelper.IsFuzzyMatch(s.DisplayName, searchQuery))
            ).ToList();
        }

        return batches.OrderByDescending(b => b.BatchId).ToList();
    }

    private static string ExtractBatchId(string programType, string notes)
    {
        string combined = $"{programType} {notes}";
        var match = System.Text.RegularExpressions.Regex.Match(combined, @"(?:دفعة|الدفعة|batch|دفعه)\s*[:#\-]?\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;
        return string.Empty;
    }

    public async Task<int> CreateOrUpdatePart141BatchAsync(Part141Batch batch)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        if (batch.Id > 0)
        {
            cmd.CommandText = @"
                UPDATE Part141Batches SET
                    BatchId = @bId,
                    ProgramName = @prog,
                    StartDate = @start,
                    EndDate = @end,
                    SyllabusHours = @hours,
                    TrainingOrderAttachments = @attach,
                    Notes = @notes,
                    AcademicYear = @acadYr
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", batch.Id);
        }
        else
        {
            cmd.CommandText = @"
                INSERT INTO Part141Batches
                (BatchId, ProgramName, StartDate, EndDate, SyllabusHours, TrainingOrderAttachments, Notes, AcademicYear, IsArchived, CreatedAt)
                VALUES
                (@bId, @prog, @start, @end, @hours, @attach, @notes, @acadYr, 0, @created)
                ON CONFLICT(BatchId) DO UPDATE SET
                    ProgramName = excluded.ProgramName,
                    StartDate = excluded.StartDate,
                    EndDate = excluded.EndDate,
                    SyllabusHours = excluded.SyllabusHours,
                    TrainingOrderAttachments = excluded.TrainingOrderAttachments,
                    Notes = excluded.Notes,
                    AcademicYear = excluded.AcademicYear;
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        }

        cmd.Parameters.AddWithValue("@bId", batch.BatchId);
        cmd.Parameters.AddWithValue("@prog", batch.ProgramName);
        cmd.Parameters.AddWithValue("@start", batch.StartDate.HasValue ? batch.StartDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@end", batch.EndDate.HasValue ? batch.EndDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@hours", batch.SyllabusHours);
        cmd.Parameters.AddWithValue("@attach", (object?)batch.TrainingOrderAttachments ?? string.Empty);
        cmd.Parameters.AddWithValue("@notes", (object?)batch.Notes ?? string.Empty);
        cmd.Parameters.AddWithValue("@acadYr", batch.AcademicYear);

        if (batch.Id > 0)
        {
            await cmd.ExecuteNonQueryAsync();
            return batch.Id;
        }
        else
        {
            var res = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(res);
        }
    }

    public async Task<bool> DeletePart141BatchAsync(int batchId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Part141Batches SET IsArchived = 1, ArchivedAt = @now WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@id", batchId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Retrieves ETP / Route Flying batches and operational line training records,
    /// completely decoupled from Part 141.
    /// </summary>
    public async Task<List<ETPBatch>> GetETPBatchesAsync(int? academicYear = null, string? searchQuery = null)
    {
        var batches = new List<ETPBatch>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 1. Persisted ETPBatches
        using (var cmd = connection.CreateCommand())
        {
            string sql = "SELECT Id, BatchId, RouteName, AirlineCompany, StartDate, EndDate, FlightHours, Notes, AcademicYear, IsArchived, CreatedAt FROM ETPBatches WHERE (IsArchived = 0 OR IsArchived IS NULL)";
            if (academicYear.HasValue && academicYear.Value > 0)
            {
                sql += " AND AcademicYear = @yr";
                cmd.Parameters.AddWithValue("@yr", academicYear.Value);
            }
            sql += " ORDER BY Id DESC;";
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                batches.Add(new ETPBatch
                {
                    Id = reader.GetInt32(0),
                    BatchId = reader.GetString(1),
                    RouteName = reader.GetString(2),
                    AirlineCompany = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    StartDate = ParseNullableDate(reader, 4),
                    EndDate = ParseNullableDate(reader, 5),
                    FlightHours = reader.IsDBNull(6) ? 50.0 : reader.GetDouble(6),
                    Notes = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    AcademicYear = reader.IsDBNull(8) ? 2026 : reader.GetInt32(8),
                    IsArchived = (reader.IsDBNull(9) ? 0 : reader.GetInt32(9)) == 1,
                    CreatedAt = DateTime.TryParse(reader.GetString(10), out var dt) ? dt : DateTime.Now
                });
            }
        }

        // 2. Fetch all ETP/ATP orders (strictly decoupled from Part 141)
        var etpOrders = await GetAllOrdersAsync(yearFilter: academicYear, regulatoryTrackFilter: "ETP");

        // If no explicit batches exist yet, infer default route batch from orders
        if (batches.Count == 0 && etpOrders.Count > 0)
        {
            var defaultBatch = new ETPBatch
            {
                Id = 0,
                BatchId = "ETP-01",
                RouteName = "تدريب خطوط جوية تجارية (ETP / Route Line Training)",
                AirlineCompany = "مصر للطيران / شركات الخط الجوي",
                StartDate = etpOrders.Where(o => o.EnrollmentDate.HasValue).Min(o => o.EnrollmentDate),
                EndDate = etpOrders.Where(o => o.CompletionDate.HasValue).Max(o => o.CompletionDate),
                FlightHours = 50.0,
                AcademicYear = academicYear ?? 2026,
                Orders = etpOrders,
                TotalPilots = etpOrders.Select(o => o.StudentId).Distinct().Count(),
                CompletedPilots = etpOrders.Count(o => !o.IsActive)
            };
            batches.Add(defaultBatch);
        }
        else
        {
            foreach (var b in batches)
            {
                var routeOrders = etpOrders
                    .Where(o => string.Equals(o.BatchId, b.BatchId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                b.Orders = routeOrders;
                b.TotalPilots = routeOrders.Select(o => o.StudentId).Distinct().Count();
                b.CompletedPilots = routeOrders.Where(o => !o.IsActive).Select(o => o.StudentId).Distinct().Count();
            }
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            batches = batches.Where(b =>
                ArabicTextHelper.IsFuzzyMatch(b.BatchId, searchQuery) ||
                ArabicTextHelper.IsFuzzyMatch(b.RouteName, searchQuery) ||
                ArabicTextHelper.IsFuzzyMatch(b.AirlineCompany, searchQuery)).ToList();
        }

        return batches;
    }

    public async Task<int> CreateOrUpdateETPBatchAsync(ETPBatch batch)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        if (batch.Id > 0)
        {
            cmd.CommandText = @"
                UPDATE ETPBatches SET
                    BatchId = @bId,
                    RouteName = @route,
                    AirlineCompany = @air,
                    StartDate = @start,
                    EndDate = @end,
                    FlightHours = @hours,
                    Notes = @notes,
                    AcademicYear = @acadYr
                WHERE Id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", batch.Id);
        }
        else
        {
            cmd.CommandText = @"
                INSERT INTO ETPBatches
                (BatchId, RouteName, AirlineCompany, StartDate, EndDate, FlightHours, Notes, AcademicYear, IsArchived, CreatedAt)
                VALUES
                (@bId, @route, @air, @start, @end, @hours, @notes, @acadYr, 0, @created)
                ON CONFLICT(BatchId) DO UPDATE SET
                    RouteName = excluded.RouteName,
                    AirlineCompany = excluded.AirlineCompany,
                    StartDate = excluded.StartDate,
                    EndDate = excluded.EndDate,
                    FlightHours = excluded.FlightHours,
                    Notes = excluded.Notes,
                    AcademicYear = excluded.AcademicYear;
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        }

        cmd.Parameters.AddWithValue("@bId", batch.BatchId);
        cmd.Parameters.AddWithValue("@route", batch.RouteName);
        cmd.Parameters.AddWithValue("@air", (object?)batch.AirlineCompany ?? string.Empty);
        cmd.Parameters.AddWithValue("@start", batch.StartDate.HasValue ? batch.StartDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@end", batch.EndDate.HasValue ? batch.EndDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@hours", batch.FlightHours);
        cmd.Parameters.AddWithValue("@notes", (object?)batch.Notes ?? string.Empty);
        cmd.Parameters.AddWithValue("@acadYr", batch.AcademicYear);

        if (batch.Id > 0)
        {
            await cmd.ExecuteNonQueryAsync();
            return batch.Id;
        }
        else
        {
            var res = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(res);
        }
    }
}
