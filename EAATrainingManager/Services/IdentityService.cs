using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EAATrainingManager.Models;
using Microsoft.Data.Sqlite;

namespace EAATrainingManager.Services;

public sealed class IdentityService
{
    private const int PasswordIterations = 210_000;
    private const int PasswordSaltSize = 16;
    private const int PasswordHashSize = 32;
    private const int DefaultSessionHours = 8;
    private readonly DatabaseService _database;

    private static readonly RolePolicy[] RolePolicies =
    [
        new("administrator", "Administrator / مسؤول النظام", [new("*", PermissionLevel.Approve)]),
        new("training_manager", "Training Manager / مدير التدريب", [
            new("students", PermissionLevel.Approve), new("training-orders", PermissionLevel.Approve),
            new("schedule", PermissionLevel.FullEdit), new("flight-records", PermissionLevel.FullEdit),
            new("assessments", PermissionLevel.Approve), new("resources", PermissionLevel.FullEdit),
            new("compliance", PermissionLevel.FullEdit), new("audit", PermissionLevel.ReadOnly),
            new("users", PermissionLevel.ReadOnly), new("locations", PermissionLevel.ReadOnly), new("personnel", PermissionLevel.FullEdit),
            new("dashboard", PermissionLevel.ReadOnly), new("notifications", PermissionLevel.FullEdit), new("curriculum", PermissionLevel.Approve)]),
        new("dispatch", "Dispatch / العمليات", [
            new("students", PermissionLevel.ReadOnly), new("training-orders", PermissionLevel.ReadOnly),
            new("schedule", PermissionLevel.Approve), new("resources", PermissionLevel.FullEdit),
            new("compliance", PermissionLevel.ReadOnly), new("audit", PermissionLevel.ReadOnly), new("notifications", PermissionLevel.FullEdit)]),
        new("instructor", "Instructor / مدرب", [
            new("students", PermissionLevel.ReadOnly), new("schedule", PermissionLevel.ReadOnly),
            new("flight-records", PermissionLevel.FullEdit), new("assessments", PermissionLevel.FullEdit),
            new("resources", PermissionLevel.ReadOnly), new("compliance", PermissionLevel.ReadOnly), new("personnel", PermissionLevel.ReadOnly),
            new("curriculum", PermissionLevel.ReadOnly)]),
        new("examiner", "Examiner / ممتحن", [
            new("students", PermissionLevel.ReadOnly), new("flight-records", PermissionLevel.ReadOnly),
            new("assessments", PermissionLevel.Approve), new("audit", PermissionLevel.ReadOnly), new("curriculum", PermissionLevel.ReadOnly)]),
        new("finance", "Finance / مالية", [
            new("students", PermissionLevel.ReadOnly), new("training-orders", PermissionLevel.ReadOnly),
            new("finance", PermissionLevel.FullEdit), new("audit", PermissionLevel.ReadOnly)]),
        new("maintenance", "Maintenance / صيانة", [
            new("resources", PermissionLevel.FullEdit), new("schedule", PermissionLevel.ReadOnly),
            new("flight-records", PermissionLevel.ReadOnly)]),
        new("student", "Student / متدرب", [new("official-records", PermissionLevel.Approve)]),
        new("auditor", "Auditor / مراجع", [new("*", PermissionLevel.ReadOnly)]),
        new("regulatory_compliance", "Regulatory Compliance / مسؤول الامتثال", [
            new("regulatory-documents", PermissionLevel.Approve), new("curriculum", PermissionLevel.Approve),
            new("compliance", PermissionLevel.Approve), new("audit", PermissionLevel.ReadOnly)])
    ];

    private static readonly (string Code, string DisplayName)[] DefaultLocations =
    [
        ("MFC", "Misr Flying College / كلية مصر للطيران"),
        ("OCT", "October Airport / مطار أكتوبر"),
        ("TOR", "El-Tor / الطور"),
        ("ASY", "Asyut / أسيوط")
    ];

    public UserSession? CurrentSession { get; private set; }

    public IdentityService(DatabaseService database)
    {
        _database = database;
    }

    public async Task EnsureAuthorizationCatalogAsync()
    {
        await _database.InitializeAsync();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var rolePolicy in RolePolicies)
        {
            int roleId = await GetOrCreateRoleAsync(connection, transaction, rolePolicy.Key, rolePolicy.DisplayName);
            foreach (var grant in rolePolicy.Grants)
            {
                int permissionId = await GetOrCreatePermissionAsync(connection, transaction, grant.Resource, grant.Level);
                using var grantCommand = connection.CreateCommand();
                grantCommand.Transaction = transaction;
                grantCommand.CommandText = "INSERT OR IGNORE INTO RolePermissions (RoleId, PermissionId) VALUES (@roleId, @permissionId);";
                grantCommand.Parameters.AddWithValue("@roleId", roleId);
                grantCommand.Parameters.AddWithValue("@permissionId", permissionId);
                await grantCommand.ExecuteNonQueryAsync();
            }
        }

        foreach (var location in DefaultLocations)
        {
            using var locationCommand = connection.CreateCommand();
            locationCommand.Transaction = transaction;
            locationCommand.CommandText = @"
                INSERT OR IGNORE INTO Locations (LocationCode, DisplayName, IsActive, CreatedAt)
                VALUES (@code, @displayName, 1, @createdAt);
            ";
            locationCommand.Parameters.AddWithValue("@code", location.Code);
            locationCommand.Parameters.AddWithValue("@displayName", location.DisplayName);
            locationCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            await locationCommand.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    public async Task<int> CreateBootstrapAdministratorAsync(string userName, string displayName, string password)
    {
        ValidateUserInput(userName, displayName, password);
        await EnsureAuthorizationCatalogAsync();

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM Users;";
            if (Convert.ToInt32(await countCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidOperationException("Bootstrap administrator creation is only allowed before the first user exists.");
        }

        int userId = await InsertUserAsync(connection, transaction, userName, displayName, password);
        int administratorRoleId = await GetRoleIdAsync(connection, transaction, "administrator");
        await AssignRoleAsync(connection, transaction, userId, administratorRoleId, null);
        var locationIds = new List<int>();
        using (var locationsCommand = connection.CreateCommand())
        {
            locationsCommand.Transaction = transaction;
            locationsCommand.CommandText = "SELECT Id FROM Locations WHERE IsActive = 1;";
            using var reader = await locationsCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync()) locationIds.Add(reader.GetInt32(0));
        }
        foreach (int locationId in locationIds)
            await AssignLocationAsync(connection, transaction, userId, locationId, null);
        await InsertAuditAsync(connection, transaction, "User", userId, "BootstrapAdministratorCreated", "First local administrator account created.", "Bootstrap");
        transaction.Commit();
        return userId;
    }

    public async Task<bool> HasAnyUsersAsync()
    {
        await _database.InitializeAsync();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<UserSession?> AuthenticateAsync(string userName, string password, string? locationCode = null, string clientName = "EAA-TMS Desktop")
    {
        CurrentSession = null;
        await EnsureAuthorizationCatalogAsync();
        using var connection = await _database.OpenIdentityConnectionAsync();
        int userId = 0;
        string storedHash = string.Empty;
        string displayName = string.Empty;
        bool active = false;

        using (var userCommand = connection.CreateCommand())
        {
            userCommand.CommandText = "SELECT Id, DisplayName, PasswordHash, IsActive FROM Users WHERE UserName = @userName COLLATE NOCASE LIMIT 1;";
            userCommand.Parameters.AddWithValue("@userName", userName.Trim());
            using var reader = await userCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                userId = reader.GetInt32(0);
                displayName = reader.GetString(1);
                storedHash = reader.GetString(2);
                active = reader.GetInt32(3) == 1;
            }
        }

        if (userId == 0 || !active || !VerifyPassword(password, storedHash))
        {
            await _database.RecordAuditEventAsync("User", userId, "LoginFailed", "Authentication failed.", userName.Trim());
            return null;
        }

        if (string.IsNullOrWhiteSpace(locationCode))
        {
            await _database.RecordAuditEventAsync("User", userId, "LoginFailed", "No operating location was selected.", userName.Trim());
            return null;
        }

        int? locationId = null;
        string? normalizedLocationCode = null;
        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            using var locationCommand = connection.CreateCommand();
            locationCommand.CommandText = @"
                SELECT l.Id, l.LocationCode
                FROM Locations l
                INNER JOIN UserLocations ul ON ul.LocationId = l.Id
                WHERE ul.UserId = @userId AND l.LocationCode = @code COLLATE NOCASE AND l.IsActive = 1
                LIMIT 1;
            ";
            locationCommand.Parameters.AddWithValue("@userId", userId);
            locationCommand.Parameters.AddWithValue("@code", locationCode.Trim());
            using var locationReader = await locationCommand.ExecuteReaderAsync();
            if (!await locationReader.ReadAsync())
            {
                await _database.RecordAuditEventAsync("User", userId, "LoginFailed", "Authentication used an inactive or unknown location.", userName.Trim());
                return null;
            }
            locationId = locationReader.GetInt32(0);
            normalizedLocationCode = locationReader.GetString(1);
        }

        DateTime startedAt = DateTime.UtcNow;
        DateTime expiresAt = startedAt.AddHours(DefaultSessionHours);
        string sessionId = Guid.NewGuid().ToString("N");
        using (var transaction = connection.BeginTransaction())
        {
            using var sessionCommand = connection.CreateCommand();
            sessionCommand.Transaction = transaction;
            sessionCommand.CommandText = @"
                INSERT INTO UserSessions (SessionId, UserId, LocationId, StartedAt, ExpiresAt, ClientName)
                VALUES (@sessionId, @userId, @locationId, @startedAt, @expiresAt, @clientName);
            ";
            sessionCommand.Parameters.AddWithValue("@sessionId", sessionId);
            sessionCommand.Parameters.AddWithValue("@userId", userId);
            sessionCommand.Parameters.AddWithValue("@locationId", locationId.HasValue ? locationId.Value : DBNull.Value);
            sessionCommand.Parameters.AddWithValue("@startedAt", startedAt.ToString("o", CultureInfo.InvariantCulture));
            sessionCommand.Parameters.AddWithValue("@expiresAt", expiresAt.ToString("o", CultureInfo.InvariantCulture));
            sessionCommand.Parameters.AddWithValue("@clientName", string.IsNullOrWhiteSpace(clientName) ? "EAA-TMS Desktop" : clientName.Trim());
            await sessionCommand.ExecuteNonQueryAsync();
            await InsertAuditAsync(connection, transaction, "UserSession", userId, "Login", "User session started.", userName.Trim(), userId, sessionId, locationId);
            transaction.Commit();
        }

        CurrentSession = new UserSession
        {
            SessionId = sessionId,
            UserId = userId,
            UserName = userName.Trim(),
            DisplayName = displayName,
            LocationId = locationId,
            LocationCode = normalizedLocationCode,
            StartedAt = startedAt,
            ExpiresAt = expiresAt
        };
        return CurrentSession;
    }

    public async Task<UserSession?> GetActiveSessionAsync(string sessionId)
    {
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT us.SessionId, us.UserId, u.UserName, u.DisplayName, us.LocationId, l.LocationCode, us.StartedAt, us.ExpiresAt
            FROM UserSessions us
            INNER JOIN Users u ON u.Id = us.UserId
            LEFT JOIN Locations l ON l.Id = us.LocationId
            WHERE us.SessionId = @sessionId AND us.EndedAt IS NULL AND us.ExpiresAt > @now AND u.IsActive = 1
            LIMIT 1;
        ";
        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new UserSession
        {
            SessionId = reader.GetString(0),
            UserId = reader.GetInt32(1),
            UserName = reader.GetString(2),
            DisplayName = reader.GetString(3),
            LocationId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            LocationCode = reader.IsDBNull(5) ? null : reader.GetString(5),
            StartedAt = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            ExpiresAt = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
        };
    }

    public async Task<bool> HasPermissionAsync(string sessionId, string resource, PermissionLevel requiredLevel)
    {
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COALESCE(MAX(p.Level), 0)
            FROM UserSessions us
            INNER JOIN Users u ON u.Id = us.UserId
            INNER JOIN UserRoles ur ON ur.UserId = u.Id
            INNER JOIN RolePermissions rp ON rp.RoleId = ur.RoleId
            INNER JOIN Permissions p ON p.Id = rp.PermissionId
            WHERE us.SessionId = @sessionId
              AND us.EndedAt IS NULL
              AND us.ExpiresAt > @now
              AND u.IsActive = 1
              AND (p.Resource = @resource COLLATE NOCASE OR p.Resource = '*');
        ";
        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@resource", resource);
        int grantedLevel = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        return grantedLevel >= (int)requiredLevel && requiredLevel != PermissionLevel.None;
    }

    public async Task RequirePermissionAsync(string sessionId, string resource, PermissionLevel requiredLevel)
    {
        if (!await HasPermissionAsync(sessionId, resource, requiredLevel))
            throw new UnauthorizedAccessException($"Permission denied for '{resource}' at level '{requiredLevel}'.");
    }

    public async Task RequireCurrentPermissionAsync(string resource, PermissionLevel requiredLevel)
    {
        UserSession? session = CurrentSession;
        if (session == null)
            throw new UnauthorizedAccessException("An authenticated user session is required.");
        await RequirePermissionAsync(session.SessionId, resource, requiredLevel);
    }

    public async Task RequireOperationPermissionAsync(string operation, string entityType)
    {
        (string Resource, PermissionLevel Level) permission = (entityType, operation) switch
        {
            ("Database", _) => ("system-admin", PermissionLevel.Approve),
            ("Student", "Student.Merge" or "Student.MergeDeleteSource") => ("students", PermissionLevel.Approve),
            ("Student", _) => ("students", PermissionLevel.FullEdit),
            ("TrainingOrder", "TrainingOrder.Complete" or "TrainingOrder.SetCompletionDate") => ("training-orders", PermissionLevel.Approve),
            ("TrainingOrder", _) => ("training-orders", PermissionLevel.FullEdit),
            ("TrainingSession", "TrainingSession.CompleteFromFlight") => ("flight-records", PermissionLevel.FullEdit),
            ("TrainingSession", _) => ("schedule", PermissionLevel.FullEdit),
            ("FlightRecord", _) => ("flight-records", PermissionLevel.FullEdit),
            ("AircraftResource", "AircraftResource.AdvanceHobbs") => ("flight-records", PermissionLevel.FullEdit),
            ("TrainingAssessment", _) => ("assessments", PermissionLevel.FullEdit),
            ("StageCheck", _) => ("assessments", PermissionLevel.FullEdit),
            ("RemedialPlan", _) => ("assessments", PermissionLevel.FullEdit),
            ("PersonnelRecord", _) => ("personnel", PermissionLevel.FullEdit),
            ("ComplianceRecord", _) => ("compliance", PermissionLevel.FullEdit),
            ("AircraftResource", _) => ("resources", PermissionLevel.FullEdit),
            ("AvailabilityWindow", "AvailabilityWindow.CreateResource" or "AvailabilityWindow.ArchiveResource") => ("resources", PermissionLevel.FullEdit),
            ("AvailabilityWindow", _) => ("schedule", PermissionLevel.FullEdit),
            ("NotificationOutbox", _) => ("notifications", PermissionLevel.FullEdit),
            ("CurriculumTemplate" or "CurriculumVersion" or "CurriculumLesson" or "TrainingObjective", _) => ("curriculum", PermissionLevel.FullEdit),
            ("RegulatoryFramework" or "RegulatoryDocument" or "ProgramApproval" or "RequirementRule" or "ComplianceEvidence" or "RegulatoryException", _) => ("regulatory-documents", PermissionLevel.FullEdit),
            ("DispatchRelease" or "SessionException", _) => ("schedule", PermissionLevel.Approve),
            ("OfficialTrainingRecord", _) => ("official-records", PermissionLevel.Approve),
            ("Part141Batch" or "ETPBatch", _) => ("training-orders", PermissionLevel.FullEdit),
            _ => throw new UnauthorizedAccessException($"No permission mapping exists for operation '{operation}' on '{entityType}'.")
        };
        await RequireCurrentPermissionAsync(permission.Resource, permission.Level);
    }

    public async Task<int> RecordApprovalAsync(string sessionId, string entityType, int entityId, string transition, string reason, string password)
    {
        if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId));
        if (string.IsNullOrWhiteSpace(transition)) throw new ArgumentException("Approval transition is required.", nameof(transition));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 8)
            throw new ArgumentException("Approval reason must contain at least eight characters.", nameof(reason));

        string resource = entityType switch
        {
            "TrainingOrder" => "training-orders",
            "TrainingAssessment" => "assessments",
            "FlightRecord" => "flight-records",
            "CurriculumVersion" or "TrainingObjective" => "curriculum",
            "RegulatoryDocument" or "ProgramApproval" => "regulatory-documents",
            "StageCheck" or "RemedialPlan" => "assessments",
            "OfficialTrainingRecord" => "official-records",
            "AircraftResource" => "resources",
            "ComplianceRecord" => "compliance",
            _ => throw new ArgumentException($"Approval is not supported for entity type '{entityType}'.", nameof(entityType))
        };

        await RequirePermissionAsync(sessionId, resource, PermissionLevel.Approve);
        UserSession? session = await GetActiveSessionAsync(sessionId);
        if (session == null || !string.Equals(CurrentSession?.SessionId, sessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The approving session is not the active application session.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var passwordCommand = connection.CreateCommand())
        {
            passwordCommand.Transaction = transaction;
            passwordCommand.CommandText = "SELECT PasswordHash FROM Users WHERE Id = @userId AND IsActive = 1;";
            passwordCommand.Parameters.AddWithValue("@userId", session.UserId);
            string? passwordHash = Convert.ToString(await passwordCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (passwordHash == null || !VerifyPassword(password, passwordHash))
            {
                await InsertAuditAsync(connection, transaction, "Approval", entityId, "ApprovalDenied", "Re-authentication failed for approval.", session.UserName, session.UserId, session.SessionId, session.LocationId);
                transaction.Commit();
                throw new UnauthorizedAccessException("Password verification failed; approval was not recorded.");
            }
        }

        DateTime approvedAt = DateTime.UtcNow;
        DateTime validUntil = approvedAt.AddMinutes(15);
        string canonicalEvidence = $"{session.UserId}|{session.SessionId}|{session.LocationId}|{entityType}|{entityId}|{transition}|{reason.Trim()}|{approvedAt:O}";
        string evidenceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalEvidence)));
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = @"
            INSERT INTO ApprovalRecords
                (EntityType, EntityId, Transition, ApproverUserId, SignerName, SessionId, LocationId, Reason, ApprovedAt, ValidUntil, EvidenceFingerprint)
            VALUES
                (@entityType, @entityId, @transition, @userId, @signer, @sessionId, @locationId, @reason, @approvedAt, @validUntil, @fingerprint);
            SELECT last_insert_rowid();
        ";
        insertCommand.Parameters.AddWithValue("@entityType", entityType);
        insertCommand.Parameters.AddWithValue("@entityId", entityId);
        insertCommand.Parameters.AddWithValue("@transition", transition.Trim());
        insertCommand.Parameters.AddWithValue("@userId", session.UserId);
        insertCommand.Parameters.AddWithValue("@signer", session.DisplayName);
        insertCommand.Parameters.AddWithValue("@sessionId", session.SessionId);
        insertCommand.Parameters.AddWithValue("@locationId", session.LocationId.HasValue ? session.LocationId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@reason", reason.Trim());
        insertCommand.Parameters.AddWithValue("@approvedAt", approvedAt.ToString("o", CultureInfo.InvariantCulture));
        insertCommand.Parameters.AddWithValue("@validUntil", validUntil.ToString("o", CultureInfo.InvariantCulture));
        insertCommand.Parameters.AddWithValue("@fingerprint", evidenceFingerprint);
        int approvalId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "Approval", approvalId, "Approved", $"Approved {entityType} {entityId} transition {transition}.", session.UserName, session.UserId, session.SessionId, session.LocationId);
        transaction.Commit();
        return approvalId;
    }

    internal async Task<int> ConsumeApprovalAsync(SqliteConnection connection, SqliteTransaction transaction, string entityType, int entityId, string transition)
    {
        UserSession? session = CurrentSession;
        if (session == null)
            throw new UnauthorizedAccessException("An authenticated session is required to consume an approval.");
        string resource = entityType switch
        {
            "TrainingOrder" => "training-orders",
            "TrainingAssessment" => "assessments",
            "FlightRecord" => "flight-records",
            "CurriculumVersion" or "TrainingObjective" => "curriculum",
            "RegulatoryDocument" or "ProgramApproval" => "regulatory-documents",
            "StageCheck" or "RemedialPlan" => "assessments",
            "OfficialTrainingRecord" => "official-records",
            _ => throw new UnauthorizedAccessException($"No approval policy exists for '{entityType}'.")
        };
        await RequirePermissionAsync(session.SessionId, resource, PermissionLevel.Approve);

        using var approvalCommand = connection.CreateCommand();
        approvalCommand.Transaction = transaction;
        approvalCommand.CommandText = @"
            SELECT Id
            FROM ApprovalRecords ar
            WHERE ar.EntityType = @entityType
              AND ar.EntityId = @entityId
              AND ar.Transition = @transition
              AND ar.ApproverUserId = @userId
              AND ar.SessionId = @sessionId
              AND ar.ValidUntil > @now
              AND NOT EXISTS (SELECT 1 FROM ApprovalUses au WHERE au.ApprovalId = ar.Id)
            ORDER BY ar.ApprovedAt DESC, ar.Id DESC
            LIMIT 1;
        ";
        approvalCommand.Parameters.AddWithValue("@entityType", entityType);
        approvalCommand.Parameters.AddWithValue("@entityId", entityId);
        approvalCommand.Parameters.AddWithValue("@transition", transition);
        approvalCommand.Parameters.AddWithValue("@userId", session.UserId);
        approvalCommand.Parameters.AddWithValue("@sessionId", session.SessionId);
        approvalCommand.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        object? approvalValue = await approvalCommand.ExecuteScalarAsync();
        if (approvalValue == null || approvalValue == DBNull.Value)
            throw new UnauthorizedAccessException("A valid, unused approval record is required for this transition.");

        int approvalId = Convert.ToInt32(approvalValue, CultureInfo.InvariantCulture);
        using var consumeCommand = connection.CreateCommand();
        consumeCommand.Transaction = transaction;
        consumeCommand.CommandText = "INSERT INTO ApprovalUses (ApprovalId, ConsumedByUserId, SessionId, ConsumedAt) VALUES (@approvalId, @userId, @sessionId, @consumedAt);";
        consumeCommand.Parameters.AddWithValue("@approvalId", approvalId);
        consumeCommand.Parameters.AddWithValue("@userId", session.UserId);
        consumeCommand.Parameters.AddWithValue("@sessionId", session.SessionId);
        consumeCommand.Parameters.AddWithValue("@consumedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        await consumeCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "Approval", approvalId, "Consumed", $"Approval consumed for {entityType} {entityId} transition {transition}.", session.UserName, session.UserId, session.SessionId, session.LocationId);
        return approvalId;
    }

    public async Task<int> CreateUserAsync(string actorSessionId, string userName, string displayName, string password, IReadOnlyCollection<string> roleKeys, IReadOnlyCollection<string>? locationCodes = null)
    {
        await RequirePermissionAsync(actorSessionId, "users", PermissionLevel.Approve);
        ValidateUserInput(userName, displayName, password);
        if (roleKeys == null || roleKeys.Count == 0)
            throw new ArgumentException("At least one role is required.", nameof(roleKeys));
        if (locationCodes == null || locationCodes.Count == 0)
            throw new ArgumentException("At least one operating location must be assigned.", nameof(locationCodes));

        UserSession? actor = await GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        int userId = await InsertUserAsync(connection, transaction, userName, displayName, password);
        foreach (string roleKey in roleKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int roleId = await GetRoleIdAsync(connection, transaction, roleKey);
            await AssignRoleAsync(connection, transaction, userId, roleId, actor.UserId);
        }
        foreach (string locationCode in locationCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int locationId = await GetActiveLocationIdAsync(connection, transaction, locationCode);
            await AssignLocationAsync(connection, transaction, userId, locationId, actor.UserId);
        }
        await InsertAuditAsync(connection, transaction, "User", userId, "Created", "User account created.", actor.UserName, actor.UserId, actor.SessionId, actor.LocationId);
        transaction.Commit();
        return userId;
    }

    public async Task<bool> SetUserActiveAsync(string actorSessionId, int userId, bool isActive)
    {
        await RequirePermissionAsync(actorSessionId, "users", PermissionLevel.Approve);
        UserSession? actor = await GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");
        if (!isActive && actor.UserId == userId)
            throw new InvalidOperationException("The active account cannot disable itself.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = "UPDATE Users SET IsActive = @active, DisabledAt = @disabledAt WHERE Id = @userId;";
        updateCommand.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        updateCommand.Parameters.AddWithValue("@disabledAt", isActive ? DBNull.Value : DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        updateCommand.Parameters.AddWithValue("@userId", userId);
        bool updated = await updateCommand.ExecuteNonQueryAsync() > 0;
        if (updated && !isActive)
        {
            using var revokeCommand = connection.CreateCommand();
            revokeCommand.Transaction = transaction;
            revokeCommand.CommandText = "UPDATE UserSessions SET EndedAt = @endedAt WHERE UserId = @userId AND EndedAt IS NULL;";
            revokeCommand.Parameters.AddWithValue("@endedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            revokeCommand.Parameters.AddWithValue("@userId", userId);
            await revokeCommand.ExecuteNonQueryAsync();
        }
        if (updated)
            await InsertAuditAsync(connection, transaction, "User", userId, isActive ? "Enabled" : "Disabled", "User account status changed.", actor.UserName, actor.UserId, actor.SessionId, actor.LocationId);
        transaction.Commit();
        if (!isActive && CurrentSession?.UserId == userId)
            CurrentSession = null;
        return updated;
    }

    public async Task<bool> EndSessionAsync(string sessionId)
    {
        UserSession? session = await GetActiveSessionAsync(sessionId);
        if (session == null) return false;
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE UserSessions SET EndedAt = @endedAt WHERE SessionId = @sessionId AND EndedAt IS NULL;";
        command.Parameters.AddWithValue("@endedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@sessionId", sessionId);
        bool ended = await command.ExecuteNonQueryAsync() > 0;
        if (ended)
            await InsertAuditAsync(connection, transaction, "UserSession", session.UserId, "Logout", "User session ended.", session.UserName, session.UserId, session.SessionId, session.LocationId);
        transaction.Commit();
        if (string.Equals(CurrentSession?.SessionId, sessionId, StringComparison.Ordinal))
            CurrentSession = null;
        return ended;
    }

    public async Task<List<LocationRecord>> GetLocationsAsync(bool activeOnly = true)
    {
        var locations = new List<LocationRecord>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, LocationCode, DisplayName, IsActive
            FROM Locations
            WHERE (@activeOnly = 0 OR IsActive = 1)
            ORDER BY DisplayName;
        ";
        command.Parameters.AddWithValue("@activeOnly", activeOnly ? 1 : 0);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            locations.Add(new LocationRecord { Id = reader.GetInt32(0), LocationCode = reader.GetString(1), DisplayName = reader.GetString(2), IsActive = reader.GetInt32(3) == 1 });
        return locations;
    }

    public async Task<List<LocationRecord>> GetAvailableLocationsAsync(string userName)
    {
        var locations = new List<LocationRecord>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT l.Id, l.LocationCode, l.DisplayName, l.IsActive
            FROM Locations l
            INNER JOIN UserLocations ul ON ul.LocationId = l.Id
            INNER JOIN Users u ON u.Id = ul.UserId
            WHERE u.UserName = @userName COLLATE NOCASE AND u.IsActive = 1 AND l.IsActive = 1
            ORDER BY l.DisplayName;
        ";
        command.Parameters.AddWithValue("@userName", userName.Trim());
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            locations.Add(new LocationRecord { Id = reader.GetInt32(0), LocationCode = reader.GetString(1), DisplayName = reader.GetString(2), IsActive = reader.GetInt32(3) == 1 });
        return locations;
    }

    public async Task<int> CreateLocationAsync(string actorSessionId, string locationCode, string displayName)
    {
        await RequirePermissionAsync(actorSessionId, "locations", PermissionLevel.Approve);
        if (string.IsNullOrWhiteSpace(locationCode) || string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Location code and display name are required.");
        UserSession? actor = await GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO Locations (LocationCode, DisplayName, IsActive, CreatedAt)
            VALUES (@code, @displayName, 1, @createdAt);
            SELECT last_insert_rowid();
        ";
        command.Parameters.AddWithValue("@code", locationCode.Trim());
        command.Parameters.AddWithValue("@displayName", displayName.Trim());
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        int locationId = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        using (var grantCommand = connection.CreateCommand())
        {
            grantCommand.Transaction = transaction;
            grantCommand.CommandText = @"
                INSERT OR IGNORE INTO UserLocations (UserId, LocationId, AssignedAt, AssignedByUserId)
                SELECT u.Id, @locationId, @assignedAt, @assignedBy
                FROM Users u
                INNER JOIN UserRoles ur ON ur.UserId = u.Id
                INNER JOIN Roles r ON r.Id = ur.RoleId
                WHERE r.RoleKey = 'administrator' AND u.IsActive = 1;
            ";
            grantCommand.Parameters.AddWithValue("@locationId", locationId);
            grantCommand.Parameters.AddWithValue("@assignedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            grantCommand.Parameters.AddWithValue("@assignedBy", actor.UserId);
            await grantCommand.ExecuteNonQueryAsync();
        }
        await InsertAuditAsync(connection, transaction, "Location", locationId, "Created", "Operating location created.", actor.UserName, actor.UserId, actor.SessionId, actor.LocationId);
        transaction.Commit();
        return locationId;
    }

    public async Task<bool> SetLocationActiveAsync(string actorSessionId, int locationId, bool isActive)
    {
        await RequirePermissionAsync(actorSessionId, "locations", PermissionLevel.Approve);
        UserSession? actor = await GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Locations SET IsActive = @active WHERE Id = @locationId;";
        command.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        command.Parameters.AddWithValue("@locationId", locationId);
        bool changed = await command.ExecuteNonQueryAsync() > 0;
        if (changed && !isActive)
        {
            using var revokeCommand = connection.CreateCommand();
            revokeCommand.Transaction = transaction;
            revokeCommand.CommandText = "UPDATE UserSessions SET EndedAt = @endedAt WHERE LocationId = @locationId AND EndedAt IS NULL;";
            revokeCommand.Parameters.AddWithValue("@endedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            revokeCommand.Parameters.AddWithValue("@locationId", locationId);
            await revokeCommand.ExecuteNonQueryAsync();
        }
        if (changed)
            await InsertAuditAsync(connection, transaction, "Location", locationId, isActive ? "Enabled" : "Disabled", "Operating location status changed.", actor.UserName, actor.UserId, actor.SessionId, actor.LocationId);
        transaction.Commit();
        return changed;
    }

    public async Task<List<UserAccount>> GetUsersAsync(string actorSessionId)
    {
        await RequirePermissionAsync(actorSessionId, "users", PermissionLevel.ReadOnly);
        var users = new List<UserAccount>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, UserName, DisplayName, IsActive, CreatedAt FROM Users ORDER BY UserName;";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            users.Add(new UserAccount { Id = reader.GetInt32(0), UserName = reader.GetString(1), DisplayName = reader.GetString(2), IsActive = reader.GetInt32(3) == 1, CreatedAt = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture) });
        return users;
    }

    private static async Task<int> GetOrCreateRoleAsync(SqliteConnection connection, SqliteTransaction transaction, string roleKey, string displayName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO Roles (RoleKey, DisplayName) VALUES (@key, @displayName); SELECT Id FROM Roles WHERE RoleKey = @key;";
        command.Parameters.AddWithValue("@key", roleKey);
        command.Parameters.AddWithValue("@displayName", displayName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> GetOrCreatePermissionAsync(SqliteConnection connection, SqliteTransaction transaction, string resource, PermissionLevel level)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO Permissions (Resource, Level) VALUES (@resource, @level); SELECT Id FROM Permissions WHERE Resource = @resource COLLATE NOCASE AND Level = @level;";
        command.Parameters.AddWithValue("@resource", resource);
        command.Parameters.AddWithValue("@level", (int)level);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<int> GetRoleIdAsync(SqliteConnection connection, SqliteTransaction transaction, string roleKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Roles WHERE RoleKey = @key COLLATE NOCASE;";
        command.Parameters.AddWithValue("@key", roleKey);
        object? result = await command.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
            throw new ArgumentException($"Unknown role '{roleKey}'.", nameof(roleKey));
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int> InsertUserAsync(SqliteConnection connection, SqliteTransaction transaction, string userName, string displayName, string password)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO Users (UserName, DisplayName, PasswordHash, IsActive, CreatedAt)
            VALUES (@userName, @displayName, @passwordHash, 1, @createdAt);
            SELECT last_insert_rowid();
        ";
        command.Parameters.AddWithValue("@userName", userName.Trim());
        command.Parameters.AddWithValue("@displayName", displayName.Trim());
        command.Parameters.AddWithValue("@passwordHash", HashPassword(password));
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task AssignRoleAsync(SqliteConnection connection, SqliteTransaction transaction, int userId, int roleId, int? assignedByUserId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO UserRoles (UserId, RoleId, AssignedAt, AssignedByUserId) VALUES (@userId, @roleId, @assignedAt, @assignedByUserId);";
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@roleId", roleId);
        command.Parameters.AddWithValue("@assignedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@assignedByUserId", assignedByUserId.HasValue ? assignedByUserId.Value : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetActiveLocationIdAsync(SqliteConnection connection, SqliteTransaction transaction, string locationCode)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Locations WHERE LocationCode = @code COLLATE NOCASE AND IsActive = 1;";
        command.Parameters.AddWithValue("@code", locationCode.Trim());
        object? result = await command.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
            throw new ArgumentException($"Unknown or inactive location '{locationCode}'.", nameof(locationCode));
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task AssignLocationAsync(SqliteConnection connection, SqliteTransaction transaction, int userId, int locationId, int? assignedByUserId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO UserLocations (UserId, LocationId, AssignedAt, AssignedByUserId) VALUES (@userId, @locationId, @assignedAt, @assignedByUserId);";
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@locationId", locationId);
        command.Parameters.AddWithValue("@assignedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@assignedByUserId", assignedByUserId.HasValue ? assignedByUserId.Value : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertAuditAsync(SqliteConnection connection, SqliteTransaction transaction, string entityType, int entityId, string action, string summary, string actor, int? userId = null, string? sessionId = null, int? locationId = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO AuditEvents (EntityType, EntityId, Action, Summary, Actor, OccurredAt, UserId, SessionId, LocationId)
            VALUES (@entityType, @entityId, @action, @summary, @actor, @occurredAt, @userId, @sessionId, @locationId);
        ";
        command.Parameters.AddWithValue("@entityType", entityType);
        command.Parameters.AddWithValue("@entityId", entityId);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@summary", summary);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@occurredAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@userId", userId.HasValue ? userId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@sessionId", (object?)sessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@locationId", locationId.HasValue ? locationId.Value : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateUserInput(string userName, string displayName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length < 3)
            throw new ArgumentException("Username must contain at least three characters.", nameof(userName));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));
        if (password == null || password.Length < 12)
            throw new ArgumentException("Password must contain at least twelve characters.", nameof(password));
    }

    private static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(PasswordSaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, PasswordHashSize);
        return $"pbkdf2-sha256${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string encodedHash)
    {
        try
        {
            string[] parts = encodedHash.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
            int iterations = int.Parse(parts[1], CultureInfo.InvariantCulture);
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException or OverflowException)
        {
            return false;
        }
    }

    private sealed record PermissionGrant(string Resource, PermissionLevel Level);
    private sealed record RolePolicy(string Key, string DisplayName, PermissionGrant[] Grants);
}
