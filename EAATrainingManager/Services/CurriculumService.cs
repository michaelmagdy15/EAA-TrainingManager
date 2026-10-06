using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EAATrainingManager.Models;
using Microsoft.Data.Sqlite;

namespace EAATrainingManager.Services;

public sealed class CurriculumService
{
    private static readonly (string Key, string Arabic, string English)[] Streams =
    [
        ("Part61", "نظام حر", "Part 61 / Modular"),
        ("Part141", "نظام معتمد", "Part 141 / Approved Program"),
        ("ETP", "خط جوي", "ETP / Airline Pilot Track"),
        ("TypeRating", "تجديد طراز وفرق وبناء ساعات", "Type Rating & Hour Building"),
        ("Evaluation", "تقييم ومعادلات", "Evaluation & Equivalency")
    ];

    private readonly DatabaseService _database;
    private readonly IdentityService _identity;

    public CurriculumService(DatabaseService database, IdentityService identity)
    {
        _database = database;
        _identity = identity;
    }

    public async Task EnsureStreamCatalogAsync()
    {
        await _database.InitializeAsync();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var frameworkCommand = connection.CreateCommand())
        {
            frameworkCommand.Transaction = transaction;
            frameworkCommand.CommandText = @"
                INSERT OR IGNORE INTO RegulatoryFrameworks (FrameworkCode, Authority, DisplayName, CreatedAt)
                VALUES ('ECAR-ECAA', 'ECAA', 'ECAR / ECAA Controlled Reference Family', @createdAt);
            ";
            frameworkCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            await frameworkCommand.ExecuteNonQueryAsync();
        }

        foreach (var stream in Streams)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT OR IGNORE INTO CurriculumTemplates (StreamKey, ArabicName, EnglishName, IsActive, CreatedAt)
                VALUES (@key, @arabic, @english, 1, @createdAt);
            ";
            command.Parameters.AddWithValue("@key", stream.Key);
            command.Parameters.AddWithValue("@arabic", stream.Arabic);
            command.Parameters.AddWithValue("@english", stream.English);
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }
        transaction.Commit();
    }

    public async Task<List<CurriculumTemplate>> GetStreamTemplatesAsync()
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        var templates = new List<CurriculumTemplate>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, StreamKey, ArabicName, EnglishName, IsActive FROM CurriculumTemplates ORDER BY StreamKey;";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            templates.Add(new CurriculumTemplate { Id = reader.GetInt32(0), StreamKey = reader.GetString(1), ArabicName = reader.GetString(2), EnglishName = reader.GetString(3), IsActive = reader.GetInt32(4) == 1 });
        return templates;
    }

    public async Task<List<CurriculumVersion>> GetCurriculumVersionsAsync()
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        var versions = new List<CurriculumVersion>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CurriculumTemplateId, VersionLabel, EffectiveFrom, EffectiveTo, RegulatoryDocumentId, ProgramApprovalId,
                   ReviewStatus, Notes, PublishedByUserId, PublishedAt, PublicationApprovalId
            FROM CurriculumVersions
            ORDER BY EffectiveFrom DESC, Id DESC;";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(new CurriculumVersion
            {
                Id = reader.GetInt32(0),
                CurriculumTemplateId = reader.GetInt32(1),
                VersionLabel = reader.GetString(2),
                EffectiveFrom = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                EffectiveTo = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                RegulatoryDocumentId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                ProgramApprovalId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Status = Enum.TryParse(reader.GetString(7), out ReviewStatus status) ? status : ReviewStatus.NeedsRegulatoryReview,
                Notes = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                PublishedByUserId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                PublishedAt = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                PublicationApprovalId = reader.IsDBNull(11) ? null : reader.GetInt32(11)
            });
        }
        return versions;
    }

    public async Task<List<CurriculumLesson>> GetLessonsForVersionAsync(int versionId)
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        var lessons = new List<CurriculumLesson>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CurriculumVersionId, StageCode, LessonCode, ArabicTitle, EnglishTitle, SequenceNumber
            FROM CurriculumLessons
            WHERE CurriculumVersionId = @versionId
            ORDER BY SequenceNumber, Id;";
        command.Parameters.AddWithValue("@versionId", versionId);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lessons.Add(new CurriculumLesson
            {
                Id = reader.GetInt32(0),
                CurriculumVersionId = reader.GetInt32(1),
                StageCode = reader.GetString(2),
                LessonCode = reader.GetString(3),
                ArabicTitle = reader.GetString(4),
                EnglishTitle = reader.GetString(5),
                SequenceNumber = reader.GetInt32(6)
            });
        }
        return lessons;
    }

    public async Task<List<TrainingObjective>> GetObjectivesForLessonAsync(int lessonId)
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        var objectives = new List<TrainingObjective>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CurriculumLessonId, ObjectiveCode, ArabicDescription, EnglishDescription, CompletionStandard, EvidenceType
            FROM TrainingObjectives
            WHERE CurriculumLessonId = @lessonId
            ORDER BY Id;";
        command.Parameters.AddWithValue("@lessonId", lessonId);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            objectives.Add(new TrainingObjective
            {
                Id = reader.GetInt32(0),
                CurriculumLessonId = reader.GetInt32(1),
                ObjectiveCode = reader.GetString(2),
                ArabicDescription = reader.GetString(3),
                EnglishDescription = reader.GetString(4),
                CompletionStandard = reader.GetString(5),
                EvidenceType = reader.GetString(6)
            });
        }
        return objectives;
    }

    public async Task<Dictionary<int, List<string>>> GetPrerequisiteCodesForVersionAsync(int versionId)
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        var prerequisiteCodes = new Dictionary<int, List<string>>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT p.ObjectiveId, prereq.ObjectiveCode
            FROM ObjectivePrerequisites p
            JOIN TrainingObjectives src ON src.Id = p.ObjectiveId
            JOIN CurriculumLessons l ON l.Id = src.CurriculumLessonId
            JOIN TrainingObjectives prereq ON prereq.Id = p.PrerequisiteObjectiveId
            WHERE l.CurriculumVersionId = @versionId
            ORDER BY p.ObjectiveId, prereq.ObjectiveCode;";
        command.Parameters.AddWithValue("@versionId", versionId);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int objectiveId = reader.GetInt32(0);
            if (!prerequisiteCodes.TryGetValue(objectiveId, out List<string>? codes))
            {
                codes = new List<string>();
                prerequisiteCodes[objectiveId] = codes;
            }
            codes.Add(reader.GetString(1));
        }
        return prerequisiteCodes;
    }

    public async Task<int> RegisterRegulatoryDocumentAsync(string actorSessionId, RegulatoryDocument document)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "regulatory-documents", PermissionLevel.FullEdit);
        if (document.FrameworkId <= 0) throw new ArgumentException("A regulatory framework is required.", nameof(document));
        if (document.EffectiveFrom.HasValue && document.EffectiveTo.HasValue && document.EffectiveTo < document.EffectiveFrom)
            throw new ArgumentException("Document effective-to date cannot precede effective-from date.", nameof(document));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        bool sourceComplete = !string.IsNullOrWhiteSpace(document.Part)
            && !string.IsNullOrWhiteSpace(document.Issue)
            && !string.IsNullOrWhiteSpace(document.Revision)
            && !string.IsNullOrWhiteSpace(document.SourceFile)
            && !string.IsNullOrWhiteSpace(document.ApprovingAuthority)
            && document.EffectiveFrom.HasValue;
        ReviewStatus initialStatus = sourceComplete ? ReviewStatus.Draft : ReviewStatus.NeedsRegulatoryReview;

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO RegulatoryDocuments
                (FrameworkId, Part, Issue, Revision, EffectiveFrom, EffectiveTo, SourceFile, ApprovingAuthority, ReviewStatus, CreatedAt, CreatedByUserId)
            VALUES
                (@frameworkId, @part, @issue, @revision, @effectiveFrom, @effectiveTo, @sourceFile, @authority, @reviewStatus, @createdAt, @createdBy);
            SELECT last_insert_rowid();
        ";
        command.Parameters.AddWithValue("@frameworkId", document.FrameworkId);
        command.Parameters.AddWithValue("@part", document.Part.Trim());
        command.Parameters.AddWithValue("@issue", document.Issue.Trim());
        command.Parameters.AddWithValue("@revision", document.Revision.Trim());
        command.Parameters.AddWithValue("@effectiveFrom", document.EffectiveFrom.HasValue ? document.EffectiveFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("@effectiveTo", document.EffectiveTo.HasValue ? document.EffectiveTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("@sourceFile", document.SourceFile.Trim());
        command.Parameters.AddWithValue("@authority", document.ApprovingAuthority.Trim());
        command.Parameters.AddWithValue("@reviewStatus", initialStatus.ToString());
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@createdBy", actor.UserId);
        int documentId = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "RegulatoryDocument", documentId, "Registered", $"Controlled-document reference registered as {initialStatus}.", actor);
        transaction.Commit();
        return documentId;
    }

    public async Task<int> CreateProgramApprovalAsync(string actorSessionId, ProgramApproval approval)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "regulatory-documents", PermissionLevel.FullEdit);
        if (approval.CurriculumVersionId <= 0 || approval.RegulatoryDocumentId <= 0 || string.IsNullOrWhiteSpace(approval.StreamKey)
            || string.IsNullOrWhiteSpace(approval.TrainingMethod) || string.IsNullOrWhiteSpace(approval.AircraftOrSimulatorScope))
            throw new ArgumentException("Program approval must identify a stream, curriculum version, source document, method, and equipment scope.", nameof(approval));
        if (approval.ValidTo.HasValue && approval.ValidTo.Value < approval.ValidFrom)
            throw new ArgumentException("Program approval validity range is invalid.", nameof(approval));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        string documentStatus;
        using (var scopeCommand = connection.CreateCommand())
        {
            scopeCommand.Transaction = transaction;
            scopeCommand.CommandText = @"
                SELECT d.ReviewStatus FROM CurriculumVersions v
                JOIN CurriculumTemplates t ON t.Id = v.CurriculumTemplateId
                JOIN RegulatoryDocuments d ON d.Id = @documentId
                WHERE v.Id = @versionId AND t.StreamKey = @stream;
            ";
            scopeCommand.Parameters.AddWithValue("@documentId", approval.RegulatoryDocumentId);
            scopeCommand.Parameters.AddWithValue("@versionId", approval.CurriculumVersionId);
            scopeCommand.Parameters.AddWithValue("@stream", approval.StreamKey.Trim());
            object? scopeValue = await scopeCommand.ExecuteScalarAsync();
            if (scopeValue == null)
                throw new InvalidOperationException("Program approval stream/version/source references do not match.");
            documentStatus = Convert.ToString(scopeValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }
        string initialStatus = documentStatus == ReviewStatus.Approved.ToString()
            ? ReviewStatus.UnderReview.ToString()
            : ReviewStatus.NeedsRegulatoryReview.ToString();

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = @"
            INSERT INTO ProgramApprovals (StreamKey, CurriculumVersionId, RegulatoryDocumentId, TrainingMethod, AircraftOrSimulatorScope, ValidFrom, ValidTo, Limitations, ReviewStatus, CreatedAt)
            VALUES (@stream, @versionId, @documentId, @method, @scope, @from, @to, @limitations, @status, @createdAt);
            SELECT last_insert_rowid();
        ";
        insertCommand.Parameters.AddWithValue("@stream", approval.StreamKey.Trim());
        insertCommand.Parameters.AddWithValue("@versionId", approval.CurriculumVersionId);
        insertCommand.Parameters.AddWithValue("@documentId", approval.RegulatoryDocumentId);
        insertCommand.Parameters.AddWithValue("@method", approval.TrainingMethod.Trim());
        insertCommand.Parameters.AddWithValue("@scope", approval.AircraftOrSimulatorScope.Trim());
        insertCommand.Parameters.AddWithValue("@from", approval.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        insertCommand.Parameters.AddWithValue("@to", approval.ValidTo.HasValue ? approval.ValidTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@limitations", approval.Limitations.Trim());
        insertCommand.Parameters.AddWithValue("@status", initialStatus);
        insertCommand.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        int approvalId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        using var linkCommand = connection.CreateCommand();
        linkCommand.Transaction = transaction;
        linkCommand.CommandText = "UPDATE CurriculumVersions SET ProgramApprovalId = @approvalId WHERE Id = @versionId AND ReviewStatus NOT IN ('Approved', 'Superseded', 'Expired');";
        linkCommand.Parameters.AddWithValue("@approvalId", approvalId);
        linkCommand.Parameters.AddWithValue("@versionId", approval.CurriculumVersionId);
        if (await linkCommand.ExecuteNonQueryAsync() == 0)
            throw new InvalidOperationException("Published curriculum versions cannot be relinked to a program approval.");
        await InsertAuditAsync(connection, transaction, "ProgramApproval", approvalId, "Registered", $"Program approval reference registered with review status {initialStatus}.", actor);
        transaction.Commit();
        return approvalId;
    }

    public async Task SetProgramApprovalStatusAsync(string actorSessionId, int programApprovalId, ReviewStatus status)
    {
        if (status is not (ReviewStatus.UnderReview or ReviewStatus.NeedsRegulatoryReview))
            throw new ArgumentException("Program approval acceptance requires ApproveProgramApprovalAsync with a password-verified approval record.", nameof(status));
        await _identity.RequirePermissionAsync(actorSessionId, "regulatory-documents", PermissionLevel.FullEdit);
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = "SELECT ReviewStatus FROM ProgramApprovals WHERE Id = @id;";
            readCommand.Parameters.AddWithValue("@id", programApprovalId);
            object? current = await readCommand.ExecuteScalarAsync();
            if (current == null) throw new InvalidOperationException("Program approval record was not found.");
            string currentStatus = Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;
            if (currentStatus is "Approved" or "Superseded" or "Expired")
                throw new InvalidOperationException("Approved program approvals are immutable; register a new approval for changed scope.");
            if (status == ReviewStatus.UnderReview && currentStatus == ReviewStatus.UnderReview.ToString())
                throw new InvalidOperationException("Program approval is already under review.");
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = "UPDATE ProgramApprovals SET ReviewStatus = @status WHERE Id = @id;";
        updateCommand.Parameters.AddWithValue("@status", status.ToString());
        updateCommand.Parameters.AddWithValue("@id", programApprovalId);
        await updateCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "ProgramApproval", programApprovalId, status.ToString(), "Program approval review status changed.", actor);
        transaction.Commit();
    }

    public async Task SetRegulatoryDocumentStatusAsync(string actorSessionId, int documentId, ReviewStatus status, int? approvalRecordId = null)
    {
        if (status is not (ReviewStatus.UnderReview or ReviewStatus.Approved or ReviewStatus.Superseded or ReviewStatus.NeedsRegulatoryReview))
            throw new ArgumentException("Unsupported controlled-document review state.", nameof(status));
        PermissionLevel required = status is ReviewStatus.Approved or ReviewStatus.Superseded ? PermissionLevel.Approve : PermissionLevel.FullEdit;
        await _identity.RequirePermissionAsync(actorSessionId, "regulatory-documents", required);
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = @"
                SELECT ReviewStatus, Part, Issue, Revision, EffectiveFrom, EffectiveTo, SourceFile, ApprovingAuthority
                FROM RegulatoryDocuments WHERE Id = @id;
            ";
            readCommand.Parameters.AddWithValue("@id", documentId);
            using var reader = await readCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Controlled regulatory document was not found.");
            string currentStatus = reader.GetString(0);
            bool completeSource = !string.IsNullOrWhiteSpace(reader.GetString(1)) && !string.IsNullOrWhiteSpace(reader.GetString(2))
                && !string.IsNullOrWhiteSpace(reader.GetString(3)) && !reader.IsDBNull(4)
                && !string.IsNullOrWhiteSpace(reader.GetString(6)) && !string.IsNullOrWhiteSpace(reader.GetString(7));
            if (status == ReviewStatus.UnderReview && !completeSource)
                throw new InvalidOperationException("The source, part, issue, revision, effective date, and approving authority must be identified before review.");
            if (status == ReviewStatus.UnderReview && currentStatus is not ("Draft" or "NeedsRegulatoryReview"))
                throw new InvalidOperationException("Only draft/flagged controlled documents can enter review.");
            if (status == ReviewStatus.Approved)
            {
                if (currentStatus != ReviewStatus.UnderReview.ToString() || !completeSource)
                    throw new InvalidOperationException("A controlled document must have complete metadata and be under review before approval.");
                if (!approvalRecordId.HasValue)
                    throw new UnauthorizedAccessException("Controlled-document approval requires a password-verified approval record.");
                int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "RegulatoryDocument", documentId, "Approve");
                if (consumedId != approvalRecordId.Value)
                    throw new UnauthorizedAccessException("The approval evidence does not match a verified approval record for this document.");
            }
            if (status == ReviewStatus.Superseded)
            {
                if (currentStatus != ReviewStatus.Approved.ToString() || !approvalRecordId.HasValue)
                    throw new InvalidOperationException("Only an approved document can be superseded with approval evidence.");
                int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "RegulatoryDocument", documentId, "Supersede");
                if (consumedId != approvalRecordId.Value)
                    throw new UnauthorizedAccessException("The approval evidence does not match a verified approval record for this document.");
            }
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = "UPDATE RegulatoryDocuments SET ReviewStatus = @status, ApprovalRecordId = @approvalId WHERE Id = @id;";
        updateCommand.Parameters.AddWithValue("@status", status.ToString());
        updateCommand.Parameters.AddWithValue("@approvalId", approvalRecordId.HasValue ? approvalRecordId.Value : DBNull.Value);
        updateCommand.Parameters.AddWithValue("@id", documentId);
        await updateCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "RegulatoryDocument", documentId, status.ToString(), "Controlled-document review state changed.", actor);
        transaction.Commit();
    }

    public async Task ApproveProgramApprovalAsync(string actorSessionId, int programApprovalId, int approvalRecordId)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "regulatory-documents", PermissionLevel.Approve);
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var validateCommand = connection.CreateCommand())
        {
            validateCommand.Transaction = transaction;
            validateCommand.CommandText = @"
                SELECT COUNT(*) FROM ProgramApprovals pa
                JOIN RegulatoryDocuments rd ON rd.Id = pa.RegulatoryDocumentId
                WHERE pa.Id = @id AND pa.ReviewStatus = 'UnderReview'
                  AND rd.ReviewStatus = 'Approved'
                  AND date(pa.ValidFrom) <= date('now')
                  AND (pa.ValidTo IS NULL OR date(pa.ValidTo) >= date('now'));
            ";
            validateCommand.Parameters.AddWithValue("@id", programApprovalId);
            if (Convert.ToInt32(await validateCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("Program approval requires an approved controlled document and current scope.");
        }
        int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "ProgramApproval", programApprovalId, "Approve");
        if (consumedId != approvalRecordId)
            throw new UnauthorizedAccessException("The approval evidence does not match a verified approval record for this program approval.");
        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = "UPDATE ProgramApprovals SET ReviewStatus = 'Approved', ApprovalRecordId = @approvalId, ApprovedByUserId = @userId, ApprovedAt = @at WHERE Id = @id;";
        updateCommand.Parameters.AddWithValue("@approvalId", approvalRecordId);
        updateCommand.Parameters.AddWithValue("@userId", actor.UserId);
        updateCommand.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        updateCommand.Parameters.AddWithValue("@id", programApprovalId);
        await updateCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "ProgramApproval", programApprovalId, "Approved", "Program approval accepted by authorized reviewer.", actor);
        transaction.Commit();
    }

    public async Task PublishCurriculumVersionAsync(string actorSessionId, int curriculumVersionId, int approvalRecordId)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "curriculum", PermissionLevel.Approve);
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var validateCommand = connection.CreateCommand())
        {
            validateCommand.Transaction = transaction;
            validateCommand.CommandText = @"
                SELECT COUNT(*) FROM CurriculumVersions v
                JOIN RegulatoryDocuments rd ON rd.Id = v.RegulatoryDocumentId
                JOIN ProgramApprovals pa ON pa.Id = v.ProgramApprovalId
                WHERE v.Id = @id AND v.ReviewStatus IN ('Draft', 'UnderReview', 'NeedsRegulatoryReview')
                  AND rd.ReviewStatus = 'Approved' AND pa.ReviewStatus = 'Approved'
                  AND date(pa.ValidFrom) <= date('now')
                  AND (pa.ValidTo IS NULL OR date(pa.ValidTo) >= date('now'));
            ";
            validateCommand.Parameters.AddWithValue("@id", curriculumVersionId);
            if (Convert.ToInt32(await validateCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("Missing, ambiguous, expired, or unapproved source documents/program scope require regulatory review.");
        }

        int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "CurriculumVersion", curriculumVersionId, "Publish");
        if (consumedId != approvalRecordId)
            throw new UnauthorizedAccessException("The approval evidence does not match a verified approval record for this curriculum version.");
        using (var supersedeCommand = connection.CreateCommand())
        {
            supersedeCommand.Transaction = transaction;
            supersedeCommand.CommandText = "UPDATE CurriculumVersions SET ReviewStatus = 'Superseded' WHERE CurriculumTemplateId = (SELECT CurriculumTemplateId FROM CurriculumVersions WHERE Id = @id) AND Id <> @id AND ReviewStatus = 'Approved';";
            supersedeCommand.Parameters.AddWithValue("@id", curriculumVersionId);
            await supersedeCommand.ExecuteNonQueryAsync();
        }
        using var publishCommand = connection.CreateCommand();
        publishCommand.Transaction = transaction;
        publishCommand.CommandText = "UPDATE CurriculumVersions SET ReviewStatus = 'Approved', PublishedByUserId = @userId, PublishedAt = @publishedAt, PublicationApprovalId = @approvalId WHERE Id = @id;";
        publishCommand.Parameters.AddWithValue("@userId", actor.UserId);
        publishCommand.Parameters.AddWithValue("@publishedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        publishCommand.Parameters.AddWithValue("@approvalId", approvalRecordId);
        publishCommand.Parameters.AddWithValue("@id", curriculumVersionId);
        await publishCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "CurriculumVersion", curriculumVersionId, "Published", "Version published against approved source and program approval.", actor);
        transaction.Commit();
    }

    public async Task<int> CreateCurriculumVersionAsync(string actorSessionId, int templateId, string versionLabel, DateOnly effectiveFrom, DateOnly? effectiveTo, int? regulatoryDocumentId, string notes)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "curriculum", PermissionLevel.FullEdit);
        if (string.IsNullOrWhiteSpace(versionLabel)) throw new ArgumentException("Curriculum version label is required.", nameof(versionLabel));
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom) throw new ArgumentException("Effective-to cannot precede effective-from.", nameof(effectiveTo));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        ReviewStatus versionStatus = ReviewStatus.NeedsRegulatoryReview;
        if (regulatoryDocumentId.HasValue)
        {
            using var sourceConnection = await _database.OpenIdentityConnectionAsync();
            using var sourceCommand = sourceConnection.CreateCommand();
            sourceCommand.CommandText = @"
                SELECT ReviewStatus, EffectiveFrom, EffectiveTo, SourceFile, Part, Issue, Revision, ApprovingAuthority
                FROM RegulatoryDocuments WHERE Id = @id;
            ";
            sourceCommand.Parameters.AddWithValue("@id", regulatoryDocumentId.Value);
            using var reader = await sourceCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                bool completeReference = !reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(3))
                    && !string.IsNullOrWhiteSpace(reader.GetString(4)) && !string.IsNullOrWhiteSpace(reader.GetString(5))
                    && !string.IsNullOrWhiteSpace(reader.GetString(6)) && !string.IsNullOrWhiteSpace(reader.GetString(7));
                if (completeReference && reader.GetString(0) == ReviewStatus.Approved.ToString())
                    versionStatus = ReviewStatus.Draft;
            }
        }

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO CurriculumVersions (CurriculumTemplateId, VersionLabel, EffectiveFrom, EffectiveTo, RegulatoryDocumentId, ReviewStatus, Notes, CreatedAt, CreatedByUserId)
            VALUES (@templateId, @versionLabel, @effectiveFrom, @effectiveTo, @documentId, @status, @notes, @createdAt, @createdBy);
            SELECT last_insert_rowid();
        ";
        command.Parameters.AddWithValue("@templateId", templateId);
        command.Parameters.AddWithValue("@versionLabel", versionLabel.Trim());
        command.Parameters.AddWithValue("@effectiveFrom", effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@effectiveTo", effectiveTo.HasValue ? effectiveTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("@documentId", regulatoryDocumentId.HasValue ? regulatoryDocumentId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@status", versionStatus.ToString());
        command.Parameters.AddWithValue("@notes", notes.Trim());
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@createdBy", actor.UserId);
        int versionId = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "CurriculumVersion", versionId, "Created", $"Curriculum version {versionLabel} created with review status {versionStatus}.", actor);
        transaction.Commit();
        return versionId;
    }

    public async Task<int> AddLessonAsync(string actorSessionId, CurriculumLesson lesson)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "curriculum", PermissionLevel.FullEdit);
        if (string.IsNullOrWhiteSpace(lesson.StageCode) || string.IsNullOrWhiteSpace(lesson.LessonCode)
            || string.IsNullOrWhiteSpace(lesson.ArabicTitle) || string.IsNullOrWhiteSpace(lesson.EnglishTitle))
            throw new ArgumentException("Stage code, lesson code, and bilingual lesson titles are required.", nameof(lesson));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");
        await EnsureEditableVersionAsync(lesson.CurriculumVersionId);

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO CurriculumLessons (CurriculumVersionId, StageCode, LessonCode, ArabicTitle, EnglishTitle, SequenceNumber, CreatedAt)
            VALUES (@versionId, @stage, @code, @arabic, @english, @sequence, @createdAt);
            SELECT last_insert_rowid();
        ";
        command.Parameters.AddWithValue("@versionId", lesson.CurriculumVersionId);
        command.Parameters.AddWithValue("@stage", lesson.StageCode.Trim());
        command.Parameters.AddWithValue("@code", lesson.LessonCode.Trim());
        command.Parameters.AddWithValue("@arabic", lesson.ArabicTitle.Trim());
        command.Parameters.AddWithValue("@english", lesson.EnglishTitle.Trim());
        command.Parameters.AddWithValue("@sequence", lesson.SequenceNumber);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        int lessonId = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "CurriculumLesson", lessonId, "Created", $"Lesson {lesson.LessonCode} added.", actor);
        transaction.Commit();
        return lessonId;
    }

    public async Task<int> AddObjectiveAsync(string actorSessionId, TrainingObjective objective)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "curriculum", PermissionLevel.FullEdit);
        if (string.IsNullOrWhiteSpace(objective.ObjectiveCode) || string.IsNullOrWhiteSpace(objective.ArabicDescription)
            || string.IsNullOrWhiteSpace(objective.EnglishDescription) || string.IsNullOrWhiteSpace(objective.CompletionStandard)
            || string.IsNullOrWhiteSpace(objective.EvidenceType))
            throw new ArgumentException("Objective code, bilingual description, completion standard, and evidence type are required.", nameof(objective));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await EnsureLessonEditableAsync(connection, transaction, objective.CurriculumLessonId);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO TrainingObjectives (CurriculumLessonId, ObjectiveCode, ArabicDescription, EnglishDescription, CompletionStandard, EvidenceType, CreatedAt)
            VALUES (@lessonId, @code, @arabic, @english, @standard, @evidence, @createdAt);
            SELECT last_insert_rowid();
        ";
        command.Parameters.AddWithValue("@lessonId", objective.CurriculumLessonId);
        command.Parameters.AddWithValue("@code", objective.ObjectiveCode.Trim());
        command.Parameters.AddWithValue("@arabic", objective.ArabicDescription.Trim());
        command.Parameters.AddWithValue("@english", objective.EnglishDescription.Trim());
        command.Parameters.AddWithValue("@standard", objective.CompletionStandard.Trim());
        command.Parameters.AddWithValue("@evidence", objective.EvidenceType.Trim());
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        int objectiveId = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "TrainingObjective", objectiveId, "Created", $"Objective {objective.ObjectiveCode} added.", actor);
        transaction.Commit();
        return objectiveId;
    }

    public async Task AddPrerequisiteAsync(string actorSessionId, int objectiveId, int prerequisiteObjectiveId)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "curriculum", PermissionLevel.FullEdit);
        if (objectiveId == prerequisiteObjectiveId) throw new InvalidOperationException("An objective cannot require itself.");
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null) throw new UnauthorizedAccessException("The acting user session is no longer active.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = @"
                SELECT COUNT(*) FROM TrainingObjectives a
                JOIN CurriculumLessons al ON al.Id = a.CurriculumLessonId
                JOIN TrainingObjectives p ON p.Id = @prerequisiteId
                JOIN CurriculumLessons pl ON pl.Id = p.CurriculumLessonId
                WHERE a.Id = @objectiveId AND al.CurriculumVersionId = pl.CurriculumVersionId;
            ";
            versionCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
            versionCommand.Parameters.AddWithValue("@prerequisiteId", prerequisiteObjectiveId);
            if (Convert.ToInt32(await versionCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("Objective prerequisites must belong to the same curriculum version.");
        }

        using (var cycleCommand = connection.CreateCommand())
        {
            cycleCommand.Transaction = transaction;
            cycleCommand.CommandText = @"
                WITH RECURSIVE prerequisites(ObjectiveId) AS (
                    SELECT PrerequisiteObjectiveId FROM ObjectivePrerequisites WHERE ObjectiveId = @prerequisiteId
                    UNION
                    SELECT op.PrerequisiteObjectiveId FROM ObjectivePrerequisites op
                    JOIN prerequisites p ON op.ObjectiveId = p.ObjectiveId
                )
                SELECT COUNT(*) FROM prerequisites WHERE ObjectiveId = @objectiveId;
            ";
            cycleCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
            cycleCommand.Parameters.AddWithValue("@prerequisiteId", prerequisiteObjectiveId);
            if (Convert.ToInt32(await cycleCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("Adding this prerequisite would create a curriculum cycle.");
        }

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = "INSERT INTO ObjectivePrerequisites (ObjectiveId, PrerequisiteObjectiveId) VALUES (@objectiveId, @prerequisiteId);";
        insertCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
        insertCommand.Parameters.AddWithValue("@prerequisiteId", prerequisiteObjectiveId);
        await insertCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "TrainingObjective", objectiveId, "PrerequisiteAdded", $"Prerequisite objective {prerequisiteObjectiveId} linked.", actor);
        transaction.Commit();
    }

    public async Task<int> RecordObjectiveProgressAsync(string actorSessionId, int studentId, int trainingOrderId, int objectiveId, string result, string remarks, int? trainingSessionId = null, int? approvalRecordId = null)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "assessments", PermissionLevel.FullEdit);
        string[] allowedResults = ["Satisfactory", "Unsatisfactory", "Incomplete", "Remedial", "Waived"];
        if (!allowedResults.Contains(result, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unsupported objective result.", nameof(result));
        if (string.Equals(result, "Waived", StringComparison.OrdinalIgnoreCase) && !approvalRecordId.HasValue)
            throw new UnauthorizedAccessException("Objective waivers require an approval record.");
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null || !string.Equals(_identity.CurrentSession?.SessionId, actorSessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The acting session is not the active application session.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        int curriculumVersionId;
        string regulatoryReviewStatus;
        int? regulatoryDocumentId;
        string regulatoryRevision;
        using (var objectiveCommand = connection.CreateCommand())
        {
            objectiveCommand.Transaction = transaction;
            objectiveCommand.CommandText = @"
                SELECT v.Id, v.ReviewStatus, v.RegulatoryDocumentId, COALESCE(rd.Revision, '')
                FROM TrainingObjectives o
                JOIN CurriculumLessons l ON l.Id = o.CurriculumLessonId
                JOIN CurriculumVersions v ON v.Id = l.CurriculumVersionId
                LEFT JOIN RegulatoryDocuments rd ON rd.Id = v.RegulatoryDocumentId
                WHERE o.Id = @objectiveId;
            ";
            objectiveCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
            using var reader = await objectiveCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Training objective was not found.");
            curriculumVersionId = reader.GetInt32(0);
            regulatoryReviewStatus = reader.GetString(1);
            regulatoryDocumentId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            regulatoryRevision = reader.GetString(3);
        }

        using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.Transaction = transaction;
            orderCommand.CommandText = "SELECT COUNT(*) FROM TrainingOrders WHERE Id = @orderId AND StudentId = @studentId;";
            orderCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            orderCommand.Parameters.AddWithValue("@studentId", studentId);
            if (Convert.ToInt32(await orderCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("The training order does not belong to this student.");
        }

        using (var prerequisiteCommand = connection.CreateCommand())
        {
            prerequisiteCommand.Transaction = transaction;
            prerequisiteCommand.CommandText = @"
                SELECT COUNT(*)
                FROM ObjectivePrerequisites prerequisites
                WHERE prerequisites.ObjectiveId = @objectiveId
                  AND NOT EXISTS (
                      SELECT 1 FROM ObjectiveProgress progress
                      WHERE progress.StudentId = @studentId
                        AND progress.TrainingOrderId = @orderId
                        AND progress.CurriculumVersionId = @versionId
                        AND progress.ObjectiveId = prerequisites.PrerequisiteObjectiveId
                        AND progress.Result IN ('Satisfactory', 'Waived')
                  );
            ";
            prerequisiteCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
            prerequisiteCommand.Parameters.AddWithValue("@studentId", studentId);
            prerequisiteCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            prerequisiteCommand.Parameters.AddWithValue("@versionId", curriculumVersionId);
            if (Convert.ToInt32(await prerequisiteCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("Required objective prerequisites are incomplete.");
        }

        int attempt;
        using (var attemptCommand = connection.CreateCommand())
        {
            attemptCommand.Transaction = transaction;
            attemptCommand.CommandText = "SELECT COUNT(*) + 1 FROM ObjectiveProgress WHERE StudentId = @studentId AND TrainingOrderId = @orderId AND ObjectiveId = @objectiveId AND CurriculumVersionId = @versionId;";
            attemptCommand.Parameters.AddWithValue("@studentId", studentId);
            attemptCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            attemptCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
            attemptCommand.Parameters.AddWithValue("@versionId", curriculumVersionId);
            attempt = Convert.ToInt32(await attemptCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        if (string.Equals(result, "Waived", StringComparison.OrdinalIgnoreCase))
        {
            int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "TrainingObjective", objectiveId, "Waive");
            if (approvalRecordId.HasValue && consumedId != approvalRecordId.Value)
                throw new UnauthorizedAccessException("The approval evidence does not match a verified approval record for this waiver.");
        }

        ReviewStatus reviewStatus = await GetRegulatoryReviewStatusAsync(connection, transaction, curriculumVersionId);
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = @"
            INSERT INTO ObjectiveProgress
                (StudentId, TrainingOrderId, ObjectiveId, CurriculumVersionId, AttemptNumber, Result, GraderUserId, TrainingSessionId, ApprovalRecordId, EvaluatedAt, Remarks, RegulatoryReviewStatus, RegulatoryDocumentId, RegulatoryRevision)
            VALUES
                (@studentId, @orderId, @objectiveId, @versionId, @attempt, @result, @graderId, @sessionId, @approvalId, @evaluatedAt, @remarks, @reviewStatus, @documentId, @revision);
            SELECT last_insert_rowid();
        ";
        insertCommand.Parameters.AddWithValue("@studentId", studentId);
        insertCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
        insertCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
        insertCommand.Parameters.AddWithValue("@versionId", curriculumVersionId);
        insertCommand.Parameters.AddWithValue("@attempt", attempt);
        insertCommand.Parameters.AddWithValue("@result", result);
        insertCommand.Parameters.AddWithValue("@graderId", actor.UserId);
        insertCommand.Parameters.AddWithValue("@sessionId", trainingSessionId.HasValue ? trainingSessionId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@approvalId", approvalRecordId.HasValue ? approvalRecordId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@evaluatedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        insertCommand.Parameters.AddWithValue("@remarks", remarks.Trim());
        insertCommand.Parameters.AddWithValue("@reviewStatus", reviewStatus.ToString());
        insertCommand.Parameters.AddWithValue("@documentId", regulatoryDocumentId.HasValue ? regulatoryDocumentId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@revision", regulatoryRevision);
        int progressId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "ObjectiveProgress", progressId, "Graded", $"Objective {objectiveId} recorded as {result} on attempt {attempt}; regulatory review status {reviewStatus}.", actor);
        transaction.Commit();
        return progressId;
    }

    public async Task<List<ObjectiveProgress>> GetObjectiveProgressAsync(int studentId, int trainingOrderId)
    {
        await _identity.RequireCurrentPermissionAsync("assessments", PermissionLevel.ReadOnly);
        var progress = new List<ObjectiveProgress>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, StudentId, TrainingOrderId, ObjectiveId, CurriculumVersionId, AttemptNumber, Result,
                   GraderUserId, TrainingSessionId, ApprovalRecordId, EvaluatedAt, Remarks, RegulatoryReviewStatus, RegulatoryRevision
            FROM ObjectiveProgress
            WHERE StudentId = @studentId AND TrainingOrderId = @orderId
            ORDER BY EvaluatedAt, Id;
        ";
        command.Parameters.AddWithValue("@studentId", studentId);
        command.Parameters.AddWithValue("@orderId", trainingOrderId);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            progress.Add(new ObjectiveProgress
            {
                Id = reader.GetInt32(0), StudentId = reader.GetInt32(1), TrainingOrderId = reader.GetInt32(2),
                ObjectiveId = reader.GetInt32(3), CurriculumVersionId = reader.GetInt32(4), AttemptNumber = reader.GetInt32(5),
                Result = reader.GetString(6), GraderUserId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                TrainingSessionId = reader.IsDBNull(8) ? null : reader.GetInt32(8), ApprovalRecordId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                EvaluatedAt = DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture), Remarks = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                RegulatoryReviewStatus = Enum.TryParse(reader.GetString(12), out ReviewStatus status) ? status : ReviewStatus.NeedsRegulatoryReview,
                RegulatoryRevision = reader.IsDBNull(13) ? string.Empty : reader.GetString(13)
            });
        return progress;
    }

    private async Task EnsureEditableVersionAsync(int versionId)
    {
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReviewStatus FROM CurriculumVersions WHERE Id = @versionId;";
        command.Parameters.AddWithValue("@versionId", versionId);
        string? status = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        if (status == null) throw new InvalidOperationException("Curriculum version was not found.");
        if (status is "Approved" or "Superseded" or "Expired")
            throw new InvalidOperationException("Published/superseded curriculum versions are immutable; create a new version to edit.");
    }

    private static async Task EnsureLessonEditableAsync(SqliteConnection connection, SqliteTransaction transaction, int lessonId)
    {
        // Validate lesson/version state in the caller's transaction.
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ReviewStatus FROM CurriculumVersions v JOIN CurriculumLessons l ON l.CurriculumVersionId = v.Id WHERE l.Id = @lessonId;";
        command.Parameters.AddWithValue("@lessonId", lessonId);
        string? status = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        if (status == null) throw new InvalidOperationException("Curriculum lesson was not found.");
        if (status is "Approved" or "Superseded" or "Expired")
            throw new InvalidOperationException("Published/superseded curriculum versions are immutable; create a new version to edit.");
    }

    internal static async Task<ReviewStatus> GetRegulatoryReviewStatusAsync(SqliteConnection connection, SqliteTransaction transaction, int versionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT v.ReviewStatus, v.EffectiveFrom, v.EffectiveTo, rd.ReviewStatus, rd.EffectiveFrom, rd.EffectiveTo,
                   pa.ReviewStatus, pa.ValidFrom, pa.ValidTo
            FROM CurriculumVersions v
            LEFT JOIN RegulatoryDocuments rd ON rd.Id = v.RegulatoryDocumentId
            LEFT JOIN ProgramApprovals pa ON pa.Id = v.ProgramApprovalId
            WHERE v.Id = @versionId;
        ";
        command.Parameters.AddWithValue("@versionId", versionId);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return ReviewStatus.NeedsRegulatoryReview;

        DateTime today = DateTime.UtcNow.Date;
        bool syllabusApproved = reader.GetString(0) == ReviewStatus.Approved.ToString()
            && DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, out DateTime syllabusStart)
            && syllabusStart.Date <= today
            && (reader.IsDBNull(2) || !DateTime.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, out DateTime syllabusEnd) || syllabusEnd.Date >= today);
        bool documentApproved = !reader.IsDBNull(3) && reader.GetString(3) == ReviewStatus.Approved.ToString()
            && !reader.IsDBNull(4) && DateTime.TryParse(reader.GetString(4), CultureInfo.InvariantCulture, out DateTime documentStart)
            && documentStart.Date <= today
            && (reader.IsDBNull(5) || !DateTime.TryParse(reader.GetString(5), CultureInfo.InvariantCulture, out DateTime documentEnd) || documentEnd.Date >= today);
        bool programApprovalCurrent = !reader.IsDBNull(6) && reader.GetString(6) == ReviewStatus.Approved.ToString()
            && DateTime.TryParse(reader.GetString(7), CultureInfo.InvariantCulture, out DateTime approvalStart)
            && approvalStart.Date <= today
            && (reader.IsDBNull(8) || !DateTime.TryParse(reader.GetString(8), CultureInfo.InvariantCulture, out DateTime approvalEnd) || approvalEnd.Date >= today);
        return syllabusApproved && documentApproved && programApprovalCurrent ? ReviewStatus.Approved : ReviewStatus.NeedsRegulatoryReview;
    }

    private static async Task InsertAuditAsync(SqliteConnection connection, SqliteTransaction transaction, string entityType, int entityId, string action, string summary, UserSession actor)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO AuditEvents (EntityType, EntityId, Action, Summary, Actor, OccurredAt, UserId, SessionId, LocationId)
            VALUES (@type, @id, @action, @summary, @actor, @at, @userId, @session, @location);
        ";
        command.Parameters.AddWithValue("@type", entityType);
        command.Parameters.AddWithValue("@id", entityId);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@summary", summary);
        command.Parameters.AddWithValue("@actor", actor.UserName);
        command.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@userId", actor.UserId);
        command.Parameters.AddWithValue("@session", actor.SessionId);
        command.Parameters.AddWithValue("@location", actor.LocationId.HasValue ? actor.LocationId.Value : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
