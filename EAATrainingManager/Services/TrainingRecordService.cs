using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;
using EAATrainingManager.Models;
using Microsoft.Data.Sqlite;

namespace EAATrainingManager.Services;

public sealed class TrainingRecordService
{
    private static readonly string[] FailingResults = ["Unsatisfactory", "Incomplete", "Remedial"];
    private static readonly string[] StageResults = [StageCheck.ResultPass, StageCheck.ResultFail, StageCheck.ResultConditional];
    private static readonly string[] ActiveRemedialStatuses = [RemedialPlan.StatusOpen, RemedialPlan.StatusInProgress];
    private static readonly string[] CompletingResults = ["Satisfactory", "Waived"];

    private readonly DatabaseService _database;
    private readonly IdentityService _identity;

    public TrainingRecordService(DatabaseService database, IdentityService identity)
    {
        _database = database;
        _identity = identity;
    }

    public async Task<int> LinkSessionObjectivesAsync(string actorSessionId, int trainingSessionId, IReadOnlyCollection<int> objectiveIds)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "schedule", PermissionLevel.FullEdit);
        if (objectiveIds == null || objectiveIds.Count == 0)
            throw new ArgumentException("At least one curriculum objective must be linked to the session.", nameof(objectiveIds));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null || !string.Equals(_identity.CurrentSession?.SessionId, actorSessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The acting session is not the active application session.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.Transaction = transaction;
            sessionCommand.CommandText = "SELECT COUNT(*) FROM TrainingSessions WHERE Id = @sessionId;";
            sessionCommand.Parameters.AddWithValue("@sessionId", trainingSessionId);
            if (Convert.ToInt32(await sessionCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("Training session was not found.");
        }

        int linked = 0;
        foreach (int objectiveId in objectiveIds)
        {
            int lessonId;
            using (var objectiveCommand = connection.CreateCommand())
            {
                objectiveCommand.Transaction = transaction;
                objectiveCommand.CommandText = "SELECT CurriculumLessonId FROM TrainingObjectives WHERE Id = @objectiveId;";
                objectiveCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
                object? lessonValue = await objectiveCommand.ExecuteScalarAsync();
                if (lessonValue == null || lessonValue == DBNull.Value)
                    throw new InvalidOperationException($"Training objective {objectiveId} was not found.");
                lessonId = Convert.ToInt32(lessonValue, CultureInfo.InvariantCulture);
            }
            using (var linkCommand = connection.CreateCommand())
            {
                linkCommand.Transaction = transaction;
                linkCommand.CommandText = @"
                    INSERT OR IGNORE INTO TrainingSessionObjectives (TrainingSessionId, ObjectiveId, CurriculumLessonId, LinkedAt, LinkedByUserId)
                    VALUES (@sessionId, @objectiveId, @lessonId, @linkedAt, @userId);
                ";
                linkCommand.Parameters.AddWithValue("@sessionId", trainingSessionId);
                linkCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
                linkCommand.Parameters.AddWithValue("@lessonId", lessonId);
                linkCommand.Parameters.AddWithValue("@linkedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                linkCommand.Parameters.AddWithValue("@userId", actor.UserId);
                linked += await linkCommand.ExecuteNonQueryAsync();
            }
        }
        await InsertAuditAsync(connection, transaction, "TrainingSession", trainingSessionId, "ObjectivesLinked", $"Linked {linked} curriculum objectives to the scheduled session.", actor);
        transaction.Commit();
        return linked;
    }

    public async Task<int> OpenRemedialPlanAsync(string actorSessionId, int objectiveProgressId, string arabicPlan, string englishPlan, int? assignedInstructorUserId = null, DateOnly? dueDate = null)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "assessments", PermissionLevel.FullEdit);
        if (string.IsNullOrWhiteSpace(arabicPlan) || string.IsNullOrWhiteSpace(englishPlan))
            throw new ArgumentException("Remedial plans require bilingual plan descriptions.", nameof(arabicPlan));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null || !string.Equals(_identity.CurrentSession?.SessionId, actorSessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The acting session is not the active application session.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        int studentId, trainingOrderId, objectiveId, curriculumVersionId;
        string attemptResult;
        using (var progressCommand = connection.CreateCommand())
        {
            progressCommand.Transaction = transaction;
            progressCommand.CommandText = "SELECT StudentId, TrainingOrderId, ObjectiveId, CurriculumVersionId, Result FROM ObjectiveProgress WHERE Id = @id;";
            progressCommand.Parameters.AddWithValue("@id", objectiveProgressId);
            using var reader = await progressCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Objective progress record was not found.");
            studentId = reader.GetInt32(0);
            trainingOrderId = reader.GetInt32(1);
            objectiveId = reader.GetInt32(2);
            curriculumVersionId = reader.GetInt32(3);
            attemptResult = reader.GetString(4);
        }
        if (!FailingResults.Contains(attemptResult, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Remedial plans can only be opened from a failed, incomplete, or remedial objective attempt.");

        if (assignedInstructorUserId.HasValue)
        {
            using var instructorCommand = connection.CreateCommand();
            instructorCommand.Transaction = transaction;
            instructorCommand.CommandText = "SELECT COUNT(*) FROM Users WHERE Id = @userId AND IsActive = 1;";
            instructorCommand.Parameters.AddWithValue("@userId", assignedInstructorUserId.Value);
            if (Convert.ToInt32(await instructorCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("The assigned instructor account was not found or is inactive.");
        }

        using (var duplicateCommand = connection.CreateCommand())
        {
            duplicateCommand.Transaction = transaction;
            duplicateCommand.CommandText = "SELECT COUNT(*) FROM RemedialPlans WHERE StudentId = @studentId AND TrainingOrderId = @orderId AND ObjectiveId = @objectiveId AND Status IN ('Open', 'InProgress');";
            duplicateCommand.Parameters.AddWithValue("@studentId", studentId);
            duplicateCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            duplicateCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
            if (Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("An active remedial plan already exists for this objective.");
        }

        ReviewStatus regulatoryReviewStatus = await CurriculumService.GetRegulatoryReviewStatusAsync(connection, transaction, curriculumVersionId);
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = @"
            INSERT INTO RemedialPlans
                (ObjectiveProgressId, StudentId, TrainingOrderId, ObjectiveId, CurriculumVersionId, ArabicPlan, EnglishPlan, AssignedInstructorUserId, Status, DueDate, OpenedAt, RegulatoryReviewStatus, CreatedByUserId)
            VALUES
                (@progressId, @studentId, @orderId, @objectiveId, @versionId, @arabicPlan, @englishPlan, @instructorId, 'Open', @dueDate, @openedAt, @reviewStatus, @userId);
            SELECT last_insert_rowid();
        ";
        insertCommand.Parameters.AddWithValue("@progressId", objectiveProgressId);
        insertCommand.Parameters.AddWithValue("@studentId", studentId);
        insertCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
        insertCommand.Parameters.AddWithValue("@objectiveId", objectiveId);
        insertCommand.Parameters.AddWithValue("@versionId", curriculumVersionId);
        insertCommand.Parameters.AddWithValue("@arabicPlan", arabicPlan.Trim());
        insertCommand.Parameters.AddWithValue("@englishPlan", englishPlan.Trim());
        insertCommand.Parameters.AddWithValue("@instructorId", assignedInstructorUserId.HasValue ? assignedInstructorUserId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@dueDate", dueDate.HasValue ? dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@openedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        insertCommand.Parameters.AddWithValue("@reviewStatus", regulatoryReviewStatus.ToString());
        insertCommand.Parameters.AddWithValue("@userId", actor.UserId);
        int planId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "RemedialPlan", planId, "Opened", $"Remedial plan opened for objective {objectiveId} from attempt result {attemptResult}.", actor);
        transaction.Commit();
        return planId;
    }

    public async Task UpdateRemedialPlanStatusAsync(string actorSessionId, int remedialPlanId, string targetStatus, string remarks)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "assessments", PermissionLevel.FullEdit);
        if (targetStatus is not (RemedialPlan.StatusInProgress or RemedialPlan.StatusCompleted))
            throw new ArgumentException("Remedial plans support only InProgress and Completed transitions; waivers require approval evidence.", nameof(targetStatus));
        if (targetStatus == RemedialPlan.StatusCompleted && (string.IsNullOrWhiteSpace(remarks) || remarks.Trim().Length < 8))
            throw new ArgumentException("Completing a remedial plan requires completion remarks of at least eight characters.", nameof(remarks));
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null || !string.Equals(_identity.CurrentSession?.SessionId, actorSessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The acting session is not the active application session.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        string currentStatus;
        int curriculumVersionId;
        using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = "SELECT Status, CurriculumVersionId FROM RemedialPlans WHERE Id = @id;";
            readCommand.Parameters.AddWithValue("@id", remedialPlanId);
            using var reader = await readCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Remedial plan was not found.");
            currentStatus = reader.GetString(0);
            curriculumVersionId = reader.GetInt32(1);
        }
        if (currentStatus is RemedialPlan.StatusCompleted or RemedialPlan.StatusWaived)
            throw new InvalidOperationException("Closed remedial plans are immutable; open a new plan if further remediation is required.");
        if (currentStatus == RemedialPlan.StatusInProgress && targetStatus == RemedialPlan.StatusInProgress)
            throw new InvalidOperationException("The remedial plan is already in progress.");

        ReviewStatus regulatoryReviewStatus = await CurriculumService.GetRegulatoryReviewStatusAsync(connection, transaction, curriculumVersionId);
        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = @"
            UPDATE RemedialPlans
            SET Status = @status,
                CompletedAt = CASE WHEN @status = 'Completed' THEN @completedAt ELSE NULL END,
                CompletionRemarks = CASE WHEN @status = 'Completed' THEN @remarks ELSE CompletionRemarks END,
                RegulatoryReviewStatus = @reviewStatus
            WHERE Id = @id;
        ";
        updateCommand.Parameters.AddWithValue("@status", targetStatus);
        updateCommand.Parameters.AddWithValue("@completedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        updateCommand.Parameters.AddWithValue("@remarks", remarks.Trim());
        updateCommand.Parameters.AddWithValue("@reviewStatus", regulatoryReviewStatus.ToString());
        updateCommand.Parameters.AddWithValue("@id", remedialPlanId);
        await updateCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "RemedialPlan", remedialPlanId, targetStatus, $"Remedial plan moved from {currentStatus} to {targetStatus}.", actor);
        transaction.Commit();
    }

    public async Task WaiveRemedialPlanAsync(string actorSessionId, int remedialPlanId, int approvalRecordId)
    {
        await _identity.RequirePermissionAsync(actorSessionId, "assessments", PermissionLevel.Approve);
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null || !string.Equals(_identity.CurrentSession?.SessionId, actorSessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The acting session is not the active application session.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        string currentStatus;
        int curriculumVersionId;
        using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = "SELECT Status, CurriculumVersionId FROM RemedialPlans WHERE Id = @id;";
            readCommand.Parameters.AddWithValue("@id", remedialPlanId);
            using var reader = await readCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Remedial plan was not found.");
            currentStatus = reader.GetString(0);
            curriculumVersionId = reader.GetInt32(1);
        }
        if (!ActiveRemedialStatuses.Contains(currentStatus, StringComparer.Ordinal))
            throw new InvalidOperationException("Only active remedial plans can be waived.");

        int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "RemedialPlan", remedialPlanId, "Waive");
        if (consumedId != approvalRecordId)
            throw new UnauthorizedAccessException("The approval evidence does not match a verified approval record for this remedial plan.");

        ReviewStatus regulatoryReviewStatus = await CurriculumService.GetRegulatoryReviewStatusAsync(connection, transaction, curriculumVersionId);
        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = "UPDATE RemedialPlans SET Status = 'Waived', ApprovalRecordId = @approvalId, CompletedAt = @completedAt, RegulatoryReviewStatus = @reviewStatus WHERE Id = @id;";
        updateCommand.Parameters.AddWithValue("@approvalId", approvalRecordId);
        updateCommand.Parameters.AddWithValue("@completedAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        updateCommand.Parameters.AddWithValue("@reviewStatus", regulatoryReviewStatus.ToString());
        updateCommand.Parameters.AddWithValue("@id", remedialPlanId);
        await updateCommand.ExecuteNonQueryAsync();
        await InsertAuditAsync(connection, transaction, "RemedialPlan", remedialPlanId, "Waived", "Remedial plan waived with password-verified approval evidence.", actor);
        transaction.Commit();
    }

    public async Task<int> RecordStageCheckAsync(string actorSessionId, StageCheck stageCheck)
    {
        if (string.IsNullOrWhiteSpace(stageCheck.StageCode)) throw new ArgumentException("Stage code is required.", nameof(stageCheck));
        if (string.IsNullOrWhiteSpace(stageCheck.ExaminerName)) throw new ArgumentException("Examiner name is required.", nameof(stageCheck));
        if (!StageResults.Contains(stageCheck.Result, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Stage check result must be Pass, Fail, or Conditional.", nameof(stageCheck));
        if (stageCheck.Result != StageCheck.ResultPass && string.IsNullOrWhiteSpace(stageCheck.Deficiencies))
            throw new ArgumentException("Failed or conditional stage checks must record deficiencies.", nameof(stageCheck));
        PermissionLevel required = stageCheck.Result == StageCheck.ResultPass ? PermissionLevel.Approve : PermissionLevel.FullEdit;
        await _identity.RequirePermissionAsync(actorSessionId, "assessments", required);
        UserSession? actor = await _identity.GetActiveSessionAsync(actorSessionId);
        if (actor == null || !string.Equals(_identity.CurrentSession?.SessionId, actorSessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The acting session is not the active application session.");

        using var connection = await _database.OpenIdentityConnectionAsync();
        using var transaction = connection.BeginTransaction();
        using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.Transaction = transaction;
            orderCommand.CommandText = "SELECT COUNT(*) FROM TrainingOrders WHERE Id = @orderId AND StudentId = @studentId;";
            orderCommand.Parameters.AddWithValue("@orderId", stageCheck.TrainingOrderId);
            orderCommand.Parameters.AddWithValue("@studentId", stageCheck.StudentId);
            if (Convert.ToInt32(await orderCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("The training order does not belong to this student.");
        }
        using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "SELECT ReviewStatus FROM CurriculumVersions WHERE Id = @versionId;";
            versionCommand.Parameters.AddWithValue("@versionId", stageCheck.CurriculumVersionId);
            string? versionStatus = Convert.ToString(await versionCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (versionStatus == null) throw new InvalidOperationException("Curriculum version was not found.");
            if (versionStatus != ReviewStatus.Approved.ToString())
                throw new InvalidOperationException("Stage checks require a published curriculum version.");
        }
        if (stageCheck.TrainingSessionId.HasValue)
        {
            using var sessionCommand = connection.CreateCommand();
            sessionCommand.Transaction = transaction;
            sessionCommand.CommandText = "SELECT StudentId FROM TrainingSessions WHERE Id = @sessionId;";
            sessionCommand.Parameters.AddWithValue("@sessionId", stageCheck.TrainingSessionId.Value);
            object? sessionStudent = await sessionCommand.ExecuteScalarAsync();
            if (sessionStudent == null || Convert.ToInt32(sessionStudent, CultureInfo.InvariantCulture) != stageCheck.StudentId)
                throw new InvalidOperationException("The linked training session does not belong to this student.");
        }

        int attemptNumber;
        using (var attemptCommand = connection.CreateCommand())
        {
            attemptCommand.Transaction = transaction;
            attemptCommand.CommandText = "SELECT COUNT(*) + 1 FROM StageChecks WHERE StudentId = @studentId AND TrainingOrderId = @orderId AND StageCode = @stage;";
            attemptCommand.Parameters.AddWithValue("@studentId", stageCheck.StudentId);
            attemptCommand.Parameters.AddWithValue("@orderId", stageCheck.TrainingOrderId);
            attemptCommand.Parameters.AddWithValue("@stage", stageCheck.StageCode.Trim());
            attemptNumber = Convert.ToInt32(await attemptCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        int? documentId;
        string documentRevision;
        using (var documentCommand = connection.CreateCommand())
        {
            documentCommand.Transaction = transaction;
            documentCommand.CommandText = "SELECT RegulatoryDocumentId, COALESCE(rd.Revision, '') FROM CurriculumVersions v LEFT JOIN RegulatoryDocuments rd ON rd.Id = v.RegulatoryDocumentId WHERE v.Id = @versionId;";
            documentCommand.Parameters.AddWithValue("@versionId", stageCheck.CurriculumVersionId);
            using var reader = await documentCommand.ExecuteReaderAsync();
            await reader.ReadAsync();
            documentId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            documentRevision = reader.GetString(1);
        }
        ReviewStatus regulatoryReviewStatus = await CurriculumService.GetRegulatoryReviewStatusAsync(connection, transaction, stageCheck.CurriculumVersionId);

        int? approvalRecordId = null;
        if (stageCheck.Result == StageCheck.ResultPass)
        {
            if (!stageCheck.ApprovalRecordId.HasValue)
                throw new UnauthorizedAccessException("Passing a stage check requires a password-verified approval record.");
            int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "StageCheck", stageCheck.TrainingOrderId, $"Accept:{stageCheck.StageCode.Trim()}");
            if (consumedId != stageCheck.ApprovalRecordId.Value)
                throw new UnauthorizedAccessException("The approval evidence does not match a verified approval record for this stage check.");
            approvalRecordId = consumedId;
        }

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = @"
            INSERT INTO StageChecks
                (StudentId, TrainingOrderId, CurriculumVersionId, StageCode, ExaminerUserId, ExaminerName, AttemptNumber, Result, Deficiencies, RemedialReference, AssessedAt, TrainingSessionId, ApprovalRecordId, RegulatoryReviewStatus, RegulatoryDocumentId, RegulatoryRevision, CreatedByUserId)
            VALUES
                (@studentId, @orderId, @versionId, @stage, @examinerUserId, @examinerName, @attempt, @result, @deficiencies, @remedialReference, @assessedAt, @sessionId, @approvalId, @reviewStatus, @documentId, @revision, @userId);
            SELECT last_insert_rowid();
        ";
        insertCommand.Parameters.AddWithValue("@studentId", stageCheck.StudentId);
        insertCommand.Parameters.AddWithValue("@orderId", stageCheck.TrainingOrderId);
        insertCommand.Parameters.AddWithValue("@versionId", stageCheck.CurriculumVersionId);
        insertCommand.Parameters.AddWithValue("@stage", stageCheck.StageCode.Trim());
        insertCommand.Parameters.AddWithValue("@examinerUserId", stageCheck.ExaminerUserId.HasValue ? stageCheck.ExaminerUserId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@examinerName", stageCheck.ExaminerName.Trim());
        insertCommand.Parameters.AddWithValue("@attempt", attemptNumber);
        insertCommand.Parameters.AddWithValue("@result", stageCheck.Result.Trim());
        insertCommand.Parameters.AddWithValue("@deficiencies", stageCheck.Deficiencies.Trim());
        insertCommand.Parameters.AddWithValue("@remedialReference", stageCheck.RemedialReference.Trim());
        insertCommand.Parameters.AddWithValue("@assessedAt", stageCheck.AssessedAt == default ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) : stageCheck.AssessedAt.ToString("o", CultureInfo.InvariantCulture));
        insertCommand.Parameters.AddWithValue("@sessionId", stageCheck.TrainingSessionId.HasValue ? stageCheck.TrainingSessionId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@approvalId", approvalRecordId.HasValue ? approvalRecordId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@reviewStatus", regulatoryReviewStatus.ToString());
        insertCommand.Parameters.AddWithValue("@documentId", documentId.HasValue ? documentId.Value : DBNull.Value);
        insertCommand.Parameters.AddWithValue("@revision", documentRevision);
        insertCommand.Parameters.AddWithValue("@userId", actor.UserId);
        int stageCheckId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        await InsertAuditAsync(connection, transaction, "StageCheck", stageCheckId, "Recorded", $"Stage check {stageCheck.StageCode} attempt {attemptNumber} recorded as {stageCheck.Result}; regulatory review status {regulatoryReviewStatus}.", actor);
        transaction.Commit();
        return stageCheckId;
    }

    public async Task<List<RemainingRequirement>> GetRemainingRequirementsAsync(int studentId, int trainingOrderId)
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        using var connection = await _database.OpenIdentityConnectionAsync();
        string regulatoryTrack = await ReadOrderTrackAsync(connection, studentId, trainingOrderId);
        int versionId = await ResolveGoverningVersionAsync(connection, studentId, trainingOrderId, regulatoryTrack);

        var objectives = new List<(int Id, string Stage, string Lesson, string Code, string Arabic, string English)>();
        using (var objectiveCommand = connection.CreateCommand())
        {
            objectiveCommand.CommandText = @"
                SELECT o.Id, l.StageCode, l.LessonCode, o.ObjectiveCode, o.ArabicDescription, o.EnglishDescription
                FROM CurriculumLessons l
                JOIN TrainingObjectives o ON o.CurriculumLessonId = l.Id
                WHERE l.CurriculumVersionId = @versionId
                ORDER BY l.SequenceNumber, o.Id;
            ";
            objectiveCommand.Parameters.AddWithValue("@versionId", versionId);
            using var reader = await objectiveCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                objectives.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        var prerequisiteMap = new Dictionary<int, List<int>>();
        using (var prerequisiteCommand = connection.CreateCommand())
        {
            prerequisiteCommand.CommandText = @"
                SELECT op.ObjectiveId, op.PrerequisiteObjectiveId
                FROM ObjectivePrerequisites op
                JOIN TrainingObjectives o ON o.Id = op.ObjectiveId
                JOIN CurriculumLessons l ON l.Id = o.CurriculumLessonId
                WHERE l.CurriculumVersionId = @versionId;
            ";
            prerequisiteCommand.Parameters.AddWithValue("@versionId", versionId);
            using var reader = await prerequisiteCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int objectiveId = reader.GetInt32(0);
                if (!prerequisiteMap.TryGetValue(objectiveId, out List<int>? prerequisites))
                    prerequisiteMap[objectiveId] = prerequisites = [];
                prerequisites.Add(reader.GetInt32(1));
            }
        }

        var attemptMap = new Dictionary<int, List<ObjectiveProgress>>();
        using (var progressCommand = connection.CreateCommand())
        {
            progressCommand.CommandText = "SELECT Id, ObjectiveId, AttemptNumber, Result, EvaluatedAt FROM ObjectiveProgress WHERE StudentId = @studentId AND TrainingOrderId = @orderId AND CurriculumVersionId = @versionId ORDER BY Id;";
            progressCommand.Parameters.AddWithValue("@studentId", studentId);
            progressCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            progressCommand.Parameters.AddWithValue("@versionId", versionId);
            using var reader = await progressCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int objectiveId = reader.GetInt32(1);
                if (!attemptMap.TryGetValue(objectiveId, out List<ObjectiveProgress>? attempts))
                    attemptMap[objectiveId] = attempts = [];
                attempts.Add(new ObjectiveProgress
                {
                    Id = reader.GetInt32(0),
                    ObjectiveId = objectiveId,
                    AttemptNumber = reader.GetInt32(2),
                    Result = reader.GetString(3),
                    EvaluatedAt = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture)
                });
            }
        }

        var openRemedials = new HashSet<int>();
        using (var remedialCommand = connection.CreateCommand())
        {
            remedialCommand.CommandText = "SELECT ObjectiveId FROM RemedialPlans WHERE StudentId = @studentId AND TrainingOrderId = @orderId AND Status IN ('Open', 'InProgress');";
            remedialCommand.Parameters.AddWithValue("@studentId", studentId);
            remedialCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            using var reader = await remedialCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync()) openRemedials.Add(reader.GetInt32(0));
        }

        bool IsCompleted(int objectiveId) =>
            attemptMap.TryGetValue(objectiveId, out List<ObjectiveProgress>? attempts) &&
            attempts.Exists(a => CompletingResults.Contains(a.Result, StringComparer.OrdinalIgnoreCase));

        var objectiveCodes = new Dictionary<int, string>();
        foreach (var objective in objectives)
            objectiveCodes[objective.Id] = objective.Code;

        var requirements = new List<RemainingRequirement>();
        foreach (var objective in objectives)
        {
            var requirement = new RemainingRequirement
            {
                ObjectiveId = objective.Id,
                StageCode = objective.Stage,
                LessonCode = objective.Lesson,
                ObjectiveCode = objective.Code,
                ArabicDescription = objective.Arabic,
                EnglishDescription = objective.English,
                HasOpenRemedial = openRemedials.Contains(objective.Id)
            };
            if (attemptMap.TryGetValue(objective.Id, out List<ObjectiveProgress>? objectiveAttempts))
            {
                requirement.Attempts = objectiveAttempts.Count;
                requirement.LastResult = objectiveAttempts[^1].Result;
            }
            if (prerequisiteMap.TryGetValue(objective.Id, out List<int>? prerequisites))
            {
                foreach (int prerequisite in prerequisites)
                {
                    if (!IsCompleted(prerequisite))
                    {
                        string prerequisiteCode = objectiveCodes.TryGetValue(prerequisite, out string? code) ? code : prerequisite.ToString(CultureInfo.InvariantCulture);
                        requirement.Blockers.Add($"Prerequisite objective {prerequisiteCode} is incomplete.");
                    }
                }
            }
            requirement.Status = IsCompleted(objective.Id) ? "Completed" : requirement.Blockers.Count > 0 ? "Blocked" : "Remaining";
            requirements.Add(requirement);
        }
        return requirements;
    }

    public async Task<List<StageCheck>> GetStageChecksAsync(int studentId, int trainingOrderId)
    {
        await _identity.RequireCurrentPermissionAsync("assessments", PermissionLevel.ReadOnly);
        var stageChecks = new List<StageCheck>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, StudentId, TrainingOrderId, CurriculumVersionId, StageCode, ExaminerUserId, ExaminerName, AttemptNumber, Result,
                   COALESCE(Deficiencies, ''), COALESCE(RemedialReference, ''), AssessedAt, TrainingSessionId, ApprovalRecordId,
                   RegulatoryReviewStatus, RegulatoryDocumentId, COALESCE(RegulatoryRevision, ''), CreatedByUserId
            FROM StageChecks
            WHERE StudentId = @studentId AND TrainingOrderId = @orderId
            ORDER BY AssessedAt, Id;
        ";
        command.Parameters.AddWithValue("@studentId", studentId);
        command.Parameters.AddWithValue("@orderId", trainingOrderId);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stageChecks.Add(new StageCheck
            {
                Id = reader.GetInt32(0),
                StudentId = reader.GetInt32(1),
                TrainingOrderId = reader.GetInt32(2),
                CurriculumVersionId = reader.GetInt32(3),
                StageCode = reader.GetString(4),
                ExaminerUserId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                ExaminerName = reader.GetString(6),
                AttemptNumber = reader.GetInt32(7),
                Result = reader.GetString(8),
                Deficiencies = reader.GetString(9),
                RemedialReference = reader.GetString(10),
                AssessedAt = DateTime.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
                TrainingSessionId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                ApprovalRecordId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                RegulatoryReviewStatus = Enum.TryParse(reader.GetString(14), out ReviewStatus status) ? status : ReviewStatus.NeedsRegulatoryReview,
                RegulatoryDocumentId = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                RegulatoryRevision = reader.GetString(16),
                CreatedByUserId = reader.IsDBNull(17) ? null : reader.GetInt32(17)
            });
        }
        return stageChecks;
    }

    public async Task<List<RemedialPlan>> GetRemedialPlansAsync(int studentId, int trainingOrderId)
    {
        await _identity.RequireCurrentPermissionAsync("assessments", PermissionLevel.ReadOnly);
        var plans = new List<RemedialPlan>();
        using var connection = await _database.OpenIdentityConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, ObjectiveProgressId, StudentId, TrainingOrderId, ObjectiveId, CurriculumVersionId, ArabicPlan, EnglishPlan,
                   AssignedInstructorUserId, Status, DueDate, OpenedAt, CompletedAt, COALESCE(CompletionRemarks, ''), ApprovalRecordId,
                   RegulatoryReviewStatus, CreatedByUserId
            FROM RemedialPlans
            WHERE StudentId = @studentId AND TrainingOrderId = @orderId
            ORDER BY OpenedAt, Id;
        ";
        command.Parameters.AddWithValue("@studentId", studentId);
        command.Parameters.AddWithValue("@orderId", trainingOrderId);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plans.Add(new RemedialPlan
            {
                Id = reader.GetInt32(0),
                ObjectiveProgressId = reader.GetInt32(1),
                StudentId = reader.GetInt32(2),
                TrainingOrderId = reader.GetInt32(3),
                ObjectiveId = reader.GetInt32(4),
                CurriculumVersionId = reader.GetInt32(5),
                ArabicPlan = reader.GetString(6),
                EnglishPlan = reader.GetString(7),
                AssignedInstructorUserId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Status = reader.GetString(9),
                DueDate = reader.IsDBNull(10) ? null : DateOnly.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                OpenedAt = DateTime.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
                CompletedAt = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture),
                CompletionRemarks = reader.GetString(13),
                ApprovalRecordId = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                RegulatoryReviewStatus = Enum.TryParse(reader.GetString(15), out ReviewStatus status) ? status : ReviewStatus.NeedsRegulatoryReview,
                CreatedByUserId = reader.IsDBNull(16) ? null : reader.GetInt32(16)
            });
        }
        return plans;
    }

    public async Task<OfficialTrainingRecord> GetOfficialTrainingRecordAsync(int studentId, int trainingOrderId)
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        using var connection = await _database.OpenIdentityConnectionAsync();
        string regulatoryTrack = await ReadOrderTrackAsync(connection, studentId, trainingOrderId);
        int versionId = await ResolveGoverningVersionAsync(connection, studentId, trainingOrderId, regulatoryTrack);

        var record = new OfficialTrainingRecord
        {
            StudentId = studentId,
            TrainingOrderId = trainingOrderId,
            RegulatoryTrack = regulatoryTrack,
            GeneratedAt = DateTime.UtcNow
        };
        using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.CommandText = "SELECT o.OrderNumber, o.ProgramType, o.Status, s.DisplayName FROM TrainingOrders o JOIN Students s ON s.Id = o.StudentId WHERE o.Id = @orderId AND o.StudentId = @studentId;";
            orderCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            orderCommand.Parameters.AddWithValue("@studentId", studentId);
            using var reader = await orderCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Training order was not found for this student.");
            record.OrderNumber = reader.GetString(0);
            record.ProgramType = reader.GetString(1);
            record.OrderStatus = reader.GetString(2);
            record.StudentName = reader.GetString(3);
        }
        using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = @"
                SELECT v.VersionLabel, v.ReviewStatus, v.EffectiveFrom, v.RegulatoryDocumentId,
                       f.FrameworkCode, d.Part, d.Issue, d.Revision, d.ApprovingAuthority, d.EffectiveFrom
                FROM CurriculumVersions v
                LEFT JOIN RegulatoryDocuments d ON d.Id = v.RegulatoryDocumentId
                LEFT JOIN RegulatoryFrameworks f ON f.Id = d.FrameworkId
                WHERE v.Id = @versionId;
            ";
            versionCommand.Parameters.AddWithValue("@versionId", versionId);
            using var reader = await versionCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                record.CurriculumVersionId = versionId;
                record.VersionLabel = reader.GetString(0);
                record.VersionStatus = Enum.TryParse(reader.GetString(1), out ReviewStatus versionStatus) ? versionStatus : ReviewStatus.NeedsRegulatoryReview;
                record.VersionEffectiveFrom = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
                record.FrameworkCode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                record.DocumentPart = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                record.DocumentIssue = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                record.DocumentRevision = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
                record.DocumentAuthority = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                record.DocumentEffectiveFrom = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture);
            }
        }

        var lessonMap = new Dictionary<int, OfficialTrainingLesson>();
        var objectiveMap = new Dictionary<int, OfficialTrainingObjective>();
        using (var lessonCommand = connection.CreateCommand())
        {
            lessonCommand.CommandText = @"
                SELECT l.Id, l.StageCode, l.LessonCode, l.ArabicTitle, l.EnglishTitle, l.SequenceNumber,
                       o.Id, o.ObjectiveCode, o.ArabicDescription, o.EnglishDescription, o.CompletionStandard, o.EvidenceType
                FROM CurriculumLessons l
                LEFT JOIN TrainingObjectives o ON o.CurriculumLessonId = l.Id
                WHERE l.CurriculumVersionId = @versionId
                ORDER BY l.SequenceNumber, o.Id;
            ";
            lessonCommand.Parameters.AddWithValue("@versionId", versionId);
            using var reader = await lessonCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int lessonId = reader.GetInt32(0);
                if (!lessonMap.TryGetValue(lessonId, out OfficialTrainingLesson? lesson))
                {
                    lesson = new OfficialTrainingLesson
                    {
                        LessonId = lessonId,
                        StageCode = reader.GetString(1),
                        LessonCode = reader.GetString(2),
                        ArabicTitle = reader.GetString(3),
                        EnglishTitle = reader.GetString(4),
                        SequenceNumber = reader.GetInt32(5)
                    };
                    lessonMap[lessonId] = lesson;
                    record.Lessons.Add(lesson);
                }
                if (reader.IsDBNull(6)) continue;
                var objective = new OfficialTrainingObjective
                {
                    ObjectiveId = reader.GetInt32(6),
                    ObjectiveCode = reader.GetString(7),
                    ArabicDescription = reader.GetString(8),
                    EnglishDescription = reader.GetString(9),
                    CompletionStandard = reader.GetString(10),
                    EvidenceType = reader.GetString(11)
                };
                lesson.Objectives.Add(objective);
                objectiveMap[objective.ObjectiveId] = objective;
            }
        }
        using (var attemptCommand = connection.CreateCommand())
        {
            attemptCommand.CommandText = @"
                SELECT Id, ObjectiveId, AttemptNumber, Result, GraderUserId, TrainingSessionId, ApprovalRecordId, EvaluatedAt, Remarks,
                       RegulatoryReviewStatus, RegulatoryRevision, CurriculumVersionId, StudentId, TrainingOrderId, RegulatoryDocumentId
                FROM ObjectiveProgress
                WHERE StudentId = @studentId AND TrainingOrderId = @orderId AND CurriculumVersionId = @versionId
                ORDER BY ObjectiveId, AttemptNumber, Id;
            ";
            attemptCommand.Parameters.AddWithValue("@studentId", studentId);
            attemptCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            attemptCommand.Parameters.AddWithValue("@versionId", versionId);
            using var reader = await attemptCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!objectiveMap.TryGetValue(reader.GetInt32(1), out OfficialTrainingObjective? objective)) continue;
                objective.Attempts.Add(new ObjectiveProgress
                {
                    Id = reader.GetInt32(0),
                    ObjectiveId = reader.GetInt32(1),
                    AttemptNumber = reader.GetInt32(2),
                    Result = reader.GetString(3),
                    GraderUserId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    TrainingSessionId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    ApprovalRecordId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    EvaluatedAt = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                    Remarks = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    RegulatoryReviewStatus = Enum.TryParse(reader.GetString(9), out ReviewStatus stamp) ? stamp : ReviewStatus.NeedsRegulatoryReview,
                    RegulatoryRevision = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    CurriculumVersionId = reader.GetInt32(11),
                    StudentId = reader.GetInt32(12),
                    TrainingOrderId = reader.GetInt32(13),
                    RegulatoryDocumentId = reader.IsDBNull(14) ? null : reader.GetInt32(14)
                });
            }
        }
        foreach (var objective in objectiveMap.Values)
        {
            objective.Status = objective.Attempts.Exists(a => CompletingResults.Contains(a.Result, StringComparer.OrdinalIgnoreCase))
                ? "Completed"
                : "Remaining";
            if (objective.Status == "Remaining" && objective.Attempts.Exists(a => FailingResults.Contains(a.Result, StringComparer.OrdinalIgnoreCase)))
                objective.Status = "Remedial";
        }

        record.StageChecks = await GetStageChecksAsync(studentId, trainingOrderId);
        record.RemedialPlans = await GetRemedialPlansAsync(studentId, trainingOrderId);
        return record;
    }

    public async Task<TrainingHourReconciliation> ReconcileTrainingHoursAsync(int studentId, int trainingOrderId)
    {
        await _identity.RequireCurrentPermissionAsync("curriculum", PermissionLevel.ReadOnly);
        using var connection = await _database.OpenIdentityConnectionAsync();
        await ReadOrderTrackAsync(connection, studentId, trainingOrderId);

        double requiredHours = 0;
        DateTime windowStart = new DateTime(2000, 1, 1);
        DateTime? completionDate = null;
        string orderNumber = string.Empty;
        using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.CommandText = "SELECT OrderNumber, EnrollmentDate, CompletionDate, SyllabusHours FROM TrainingOrders WHERE Id = @orderId AND StudentId = @studentId;";
            orderCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            orderCommand.Parameters.AddWithValue("@studentId", studentId);
            using var orderReader = await orderCommand.ExecuteReaderAsync();
            if (!await orderReader.ReadAsync()) throw new InvalidOperationException("Training order was not found for this student.");
            orderNumber = orderReader.GetString(0);
            if (!orderReader.IsDBNull(1)) windowStart = DateTime.Parse(orderReader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (!orderReader.IsDBNull(2)) completionDate = DateTime.Parse(orderReader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            requiredHours = orderReader.IsDBNull(3) ? 0 : orderReader.GetDouble(3);
        }

        DateTime windowEnd = completionDate ?? DateTime.UtcNow;
        if (windowEnd < windowStart) windowEnd = windowStart;

        var reconciliation = new TrainingHourReconciliation
        {
            StudentId = studentId,
            TrainingOrderId = trainingOrderId,
            OrderNumber = orderNumber,
            RequiredHours = Math.Round(requiredHours, 2)
        };

        using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.CommandText = "SELECT Id, StartAt, EndAt, Status FROM TrainingSessions WHERE StudentId = @studentId AND StartAt <= @windowEnd AND EndAt >= @windowStart;";
            sessionCommand.Parameters.AddWithValue("@studentId", studentId);
            sessionCommand.Parameters.AddWithValue("@windowEnd", windowEnd);
            sessionCommand.Parameters.AddWithValue("@windowStart", windowStart);
            using var sessionReader = await sessionCommand.ExecuteReaderAsync();
            while (await sessionReader.ReadAsync())
            {
                int sessionId = sessionReader.GetInt32(0);
                DateTime sessionStart = DateTime.Parse(sessionReader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                DateTime sessionEnd = DateTime.Parse(sessionReader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                string sessionStatus = sessionReader.GetString(3);
                double duration = (sessionEnd - sessionStart).TotalHours;
                if (duration <= 0)
                {
                    if (!string.Equals(sessionStatus, "Cancelled", StringComparison.Ordinal))
                        reconciliation.Issues.Add(new TrainingHourIssue
                        {
                            Code = TrainingHourIssue.CodeSessionWindow,
                            Details = $"Training session {sessionId} has a non-positive window ({sessionStart:yyyy-MM-dd HH:mm} to {sessionEnd:yyyy-MM-dd HH:mm}) / فترة الجلسة غير صالحة"
                        });
                    continue;
                }
                if (string.Equals(sessionStatus, "Scheduled", StringComparison.Ordinal))
                    reconciliation.ScheduledSessionHours += duration;
                else if (string.Equals(sessionStatus, "Completed", StringComparison.Ordinal))
                    reconciliation.CompletedSessionHours += duration;
            }
        }

        var flightRecords = new List<(int Id, int? SessionId, DateTime Start, DateTime End, double HobbsStart, double HobbsEnd)>();
        using (var flightCommand = connection.CreateCommand())
        {
            flightCommand.CommandText = "SELECT Id, TrainingSessionId, StartAt, EndAt, HobbsStart, HobbsEnd FROM FlightRecords WHERE StudentId = @studentId AND StartAt <= @windowEnd AND EndAt >= @windowStart;";
            flightCommand.Parameters.AddWithValue("@studentId", studentId);
            flightCommand.Parameters.AddWithValue("@windowEnd", windowEnd);
            flightCommand.Parameters.AddWithValue("@windowStart", windowStart);
            using var flightReader = await flightCommand.ExecuteReaderAsync();
            while (await flightReader.ReadAsync())
            {
                flightRecords.Add((
                    flightReader.GetInt32(0),
                    flightReader.IsDBNull(1) ? null : flightReader.GetInt32(1),
                    DateTime.Parse(flightReader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    DateTime.Parse(flightReader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    flightReader.GetDouble(4),
                    flightReader.GetDouble(5)));
            }
        }

        var linkedSessionIds = new List<int>();
        foreach (var flightRecord in flightRecords)
        {
            if (flightRecord.SessionId.HasValue && !linkedSessionIds.Contains(flightRecord.SessionId.Value))
                linkedSessionIds.Add(flightRecord.SessionId.Value);
        }

        var linkedSessionOwners = new Dictionary<int, int>();
        if (linkedSessionIds.Count > 0)
        {
            using var ownerCommand = connection.CreateCommand();
            ownerCommand.CommandText = "SELECT Id, StudentId FROM TrainingSessions WHERE Id IN (" + string.Join(",", linkedSessionIds) + ");";
            using var ownerReader = await ownerCommand.ExecuteReaderAsync();
            while (await ownerReader.ReadAsync())
                linkedSessionOwners[ownerReader.GetInt32(0)] = ownerReader.GetInt32(1);
        }

        foreach (var flightRecord in flightRecords)
        {
            double duration = (flightRecord.End - flightRecord.Start).TotalHours;
            double hobbsDelta = flightRecord.HobbsEnd - flightRecord.HobbsStart;
            if (duration <= 0)
            {
                reconciliation.Issues.Add(new TrainingHourIssue
                {
                    Code = TrainingHourIssue.CodeSessionWindow,
                    Details = $"Flight record {flightRecord.Id} has a non-positive window ({flightRecord.Start:yyyy-MM-dd HH:mm} to {flightRecord.End:yyyy-MM-dd HH:mm}) / فترة سجل الطيران غير صالحة"
                });
            }
            else
            {
                reconciliation.FlightRecordHours += duration;
                if (Math.Abs(hobbsDelta - duration) > 0.25 + 1e-9)
                {
                    reconciliation.Issues.Add(new TrainingHourIssue
                    {
                        Code = TrainingHourIssue.CodeHobbsVariance,
                        Details = $"Flight record {flightRecord.Id}: Hobbs {hobbsDelta:F2} h vs elapsed {duration:F2} h exceeds the 0.25 h tolerance / فرق ساعات الهوبس يتجاوز الحد المسموح"
                    });
                }
            }
            reconciliation.HobbsRecordedHours += hobbsDelta;

            if (!flightRecord.SessionId.HasValue)
            {
                reconciliation.Issues.Add(new TrainingHourIssue
                {
                    Code = TrainingHourIssue.CodeUnlinkedFlightRecord,
                    Details = $"Flight record {flightRecord.Id} is not linked to a training session / سجل الطيران غير مرتبط بجلسة تدريب"
                });
            }
            else if (linkedSessionOwners.TryGetValue(flightRecord.SessionId.Value, out int ownerStudentId) && ownerStudentId != studentId)
            {
                reconciliation.Issues.Add(new TrainingHourIssue
                {
                    Code = TrainingHourIssue.CodeSessionStudentMismatch,
                    Details = $"Flight record {flightRecord.Id} links to session {flightRecord.SessionId.Value} owned by student {ownerStudentId} / السجل مرتبط بجلسة تابعة لمتدرب آخر"
                });
            }
        }

        reconciliation.FlightRecordHours = Math.Round(reconciliation.FlightRecordHours, 2);
        reconciliation.HobbsRecordedHours = Math.Round(reconciliation.HobbsRecordedHours, 2);
        reconciliation.ScheduledSessionHours = Math.Round(reconciliation.ScheduledSessionHours, 2);
        reconciliation.CompletedSessionHours = Math.Round(reconciliation.CompletedSessionHours, 2);
        reconciliation.RemainingRequiredHours = Math.Round(Math.Max(0, requiredHours - reconciliation.FlightRecordHours), 2);
        reconciliation.IsReconciled = reconciliation.Issues.Count == 0;
        return reconciliation;
    }

    public async Task<int> AcknowledgeOfficialTrainingRecordAsync(string actorSessionId, int studentId, int trainingOrderId, string reason, string password)
    {
        UserSession? ambientSession = _identity.CurrentSession;
        if (ambientSession == null || !string.Equals(ambientSession.SessionId, actorSessionId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The acting session is not the active application session.");

        await using var connection = await _database.OpenIdentityConnectionAsync();
        using (var linkCommand = connection.CreateCommand())
        {
            linkCommand.CommandText = "SELECT StudentId FROM Users WHERE Id = @userId;";
            linkCommand.Parameters.AddWithValue("@userId", ambientSession.UserId);
            object? linkedStudentValue = await linkCommand.ExecuteScalarAsync();
            if (linkedStudentValue == null || linkedStudentValue == DBNull.Value)
                throw new InvalidOperationException("This account is not linked to a student record.");
            int linkedStudentId = Convert.ToInt32(linkedStudentValue, CultureInfo.InvariantCulture);
            if (linkedStudentId != studentId)
                throw new UnauthorizedAccessException("A student may only acknowledge their own training record.");
        }

        await ReadOrderTrackAsync(connection, studentId, trainingOrderId);

        using (var acknowledgmentCommand = connection.CreateCommand())
        {
            acknowledgmentCommand.CommandText = @"
                SELECT COUNT(*)
                FROM ApprovalRecords ar
                INNER JOIN ApprovalUses au ON au.ApprovalId = ar.Id
                WHERE ar.EntityType = 'OfficialTrainingRecord'
                  AND ar.Transition = 'Acknowledge'
                  AND ar.EntityId = @orderId;";
            acknowledgmentCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            if (Convert.ToInt32(await acknowledgmentCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("The official training record has already been acknowledged by the student.");
        }

        int approvalRecordId = await _identity.RecordApprovalAsync(actorSessionId, "OfficialTrainingRecord", trainingOrderId, "Acknowledge", reason, password);

        using var transaction = connection.BeginTransaction();
        int consumedId = await _identity.ConsumeApprovalAsync(connection, transaction, "OfficialTrainingRecord", trainingOrderId, "Acknowledge");
        if (consumedId != approvalRecordId)
            throw new UnauthorizedAccessException("The consumed acknowledgment approval does not match the recorded approval.");
        transaction.Commit();
        return approvalRecordId;
    }

    public async Task<string> ExportOfficialTrainingRecordAsync(string targetFilePath, int studentId, int trainingOrderId)
    {
        var record = await GetOfficialTrainingRecordAsync(studentId, trainingOrderId);

        var objectiveCodeById = new Dictionary<int, string>();
        var userIds = new List<int>();
        foreach (var lesson in record.Lessons)
        {
            foreach (var objective in lesson.Objectives)
            {
                objectiveCodeById[objective.ObjectiveId] = objective.ObjectiveCode;
                foreach (var attempt in objective.Attempts)
                {
                    if (attempt.GraderUserId.HasValue && !userIds.Contains(attempt.GraderUserId.Value))
                        userIds.Add(attempt.GraderUserId.Value);
                }
            }
        }
        foreach (var remedialPlan in record.RemedialPlans)
        {
            if (remedialPlan.AssignedInstructorUserId.HasValue && !userIds.Contains(remedialPlan.AssignedInstructorUserId.Value))
                userIds.Add(remedialPlan.AssignedInstructorUserId.Value);
        }

        var userNames = new Dictionary<int, string>();
        if (userIds.Count > 0)
        {
            using var connection = await _database.OpenIdentityConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, DisplayName FROM Users WHERE Id IN (" + string.Join(",", userIds) + ")";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                userNames[reader.GetInt32(0)] = reader.GetString(1);
        }

        var signatures = new List<(string LabelAr, string LabelEn, string Name)>();
        foreach (var lesson in record.Lessons)
        {
            foreach (var objective in lesson.Objectives)
            {
                foreach (var attempt in objective.Attempts)
                {
                    if (!attempt.GraderUserId.HasValue) continue;
                    if (!userNames.TryGetValue(attempt.GraderUserId.Value, out string? graderName)) continue;
                    string labelAr = "المدقق — " + objective.ObjectiveCode;
                    string labelEn = "Examiner — " + objective.ObjectiveCode;
                    if (!signatures.Any(s => s.LabelEn == labelEn)) signatures.Add((labelAr, labelEn, graderName));
                }
            }
        }

        var orderSignatures = new List<(string Transition, string? SignerName, string ApprovedAt)>();
        using (var connection = await _database.OpenIdentityConnectionAsync())
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT ar.Transition, ar.SignerName, ar.ApprovedAt
                                    FROM ApprovalRecords ar
                                    INNER JOIN ApprovalUses au ON au.ApprovalId = ar.Id
                                    WHERE (ar.EntityType IN ('StageCheck','TrainingOrder') AND ar.EntityId = @orderId)
                                       OR (ar.EntityType = 'RemedialPlan' AND ar.EntityId IN (SELECT Id FROM RemedialPlans WHERE TrainingOrderId = @orderId))
                                    ORDER BY ar.ApprovedAt";
            command.Parameters.AddWithValue("@orderId", trainingOrderId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                orderSignatures.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        }

        foreach (var orderSignature in orderSignatures)
        {
            string signerName = orderSignature.SignerName ?? "غير معروف / Unknown";
            if (orderSignature.Transition == "Complete")
            {
                signatures.Add(("اعتماد إتمام أمر التدريب", "Training Order Completion Approval", signerName));
            }
            else if (orderSignature.Transition == "Waive")
            {
                signatures.Add(("إعفاء خطة المعالجة", "Remedial Plan Waiver", signerName));
            }
            else if (orderSignature.Transition.StartsWith("Pass:", StringComparison.Ordinal) || orderSignature.Transition.StartsWith("Fail:", StringComparison.Ordinal))
            {
                string stageCodeLabel = orderSignature.Transition.Substring(5);
                signatures.Add(("فحص مرحلة — " + stageCodeLabel, "Stage Check — " + stageCodeLabel, signerName));
            }
        }

        string studentSignature = "غير موقّع / Not acknowledged";
        using (var connection = await _database.OpenIdentityConnectionAsync())
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT ar.SignerName FROM ApprovalRecords ar
                                    INNER JOIN ApprovalUses au ON au.ApprovalId = ar.Id
                                    WHERE ar.EntityType = 'OfficialTrainingRecord'
                                      AND ar.Transition = 'Acknowledge'
                                      AND ar.EntityId = @entityId
                                    ORDER BY ar.ApprovedAt DESC LIMIT 1";
            command.Parameters.AddWithValue("@entityId", trainingOrderId);
            var acknowledgmentResult = await command.ExecuteScalarAsync();
            if (acknowledgmentResult is string acknowledgedByName && !string.IsNullOrWhiteSpace(acknowledgedByName))
                studentSignature = acknowledgedByName;
        }

        string? directory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("السجل التدريبي الرسمي");
        worksheet.RightToLeft = true;

        int totalColumns = 12;
        int row = 1;

        void WriteHeaderLine(string text, int fontSize, bool bold, XLColor backgroundColor, XLColor fontColor, int height)
        {
            worksheet.Range(row, 1, row, totalColumns).Merge();
            var headerCell = worksheet.Cell(row, 1);
            headerCell.Value = text;
            headerCell.Style.Font.FontSize = fontSize;
            headerCell.Style.Font.Bold = bold;
            headerCell.Style.Font.FontName = "Segoe UI";
            headerCell.Style.Font.FontColor = fontColor;
            headerCell.Style.Fill.BackgroundColor = backgroundColor;
            headerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1F497D");
            worksheet.Row(row).Height = height;
            row++;
        }

        void WriteSectionTitle(string text)
        {
            worksheet.Range(row, 1, row, totalColumns).Merge();
            var sectionCell = worksheet.Cell(row, 1);
            sectionCell.Value = text;
            sectionCell.Style.Font.FontSize = 12;
            sectionCell.Style.Font.Bold = true;
            sectionCell.Style.Font.FontName = "Segoe UI";
            sectionCell.Style.Font.FontColor = XLColor.FromHtml("#1F497D");
            sectionCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
            sectionCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sectionCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sectionCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sectionCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1F497D");
            worksheet.Row(row).Height = 22;
            row++;
        }

        void WriteColumnHeaders(string[] headers)
        {
            for (int column = 1; column <= totalColumns; column++)
            {
                var headerCell = worksheet.Cell(row, column);
                headerCell.Value = column <= headers.Length ? headers[column - 1] : string.Empty;
                headerCell.Style.Font.FontSize = 10;
                headerCell.Style.Font.Bold = true;
                headerCell.Style.Font.FontName = "Segoe UI";
                headerCell.Style.Font.FontColor = XLColor.FromHtml("#FFFFFF");
                headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F497D");
                headerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                headerCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                headerCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#DCE6F1");
            }
            worksheet.Row(row).Height = 20;
            row++;
        }

        void WriteDataRow(string[] values, bool centeredFromColumn)
        {
            bool zebra = (row % 2) == 0;
            for (int index = 0; index < totalColumns; index++)
            {
                var dataCell = worksheet.Cell(row, index + 1);
                dataCell.Value = index < values.Length ? values[index] : string.Empty;
                dataCell.Style.Font.FontSize = 10;
                dataCell.Style.Font.FontName = "Segoe UI";
                dataCell.Style.Fill.BackgroundColor = zebra ? XLColor.FromHtml("#F8FAFC") : XLColor.FromHtml("#FFFFFF");
                dataCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#DCE6F1");
                dataCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                dataCell.Style.Alignment.Horizontal = (index == 0 || (centeredFromColumn && index >= 8)) ? XLAlignmentHorizontalValues.Center : XLAlignmentHorizontalValues.Right;
            }
            worksheet.Row(row).Height = 18;
            row++;
        }

        WriteHeaderLine("السجل التدريبي الرسمي — Egyptian Aviation Academy Training Management System", 16, true, XLColor.FromHtml("#1F497D"), XLColor.FromHtml("#FFFFFF"), 28);
        WriteHeaderLine("المتدرب / Trainee: " + record.StudentName + "    |    أمر التدريب / Training Order: " + record.OrderNumber, 11, true, XLColor.FromHtml("#DCE6F1"), XLColor.FromHtml("#1F497D"), 20);
        WriteHeaderLine("المسار / Track: " + record.RegulatoryTrack + "    |    الإصدار / Version: " + record.VersionLabel + "    |    التاريخ الفعّال / Effective: " + record.VersionEffectiveFrom.ToString("yyyy-MM-dd"), 11, false, XLColor.FromHtml("#DCE6F1"), XLColor.FromHtml("#1F497D"), 20);
        WriteHeaderLine("المستند المُنظِّم / Governing Document: " + record.FrameworkCode + " " + record.DocumentPart + " / " + record.DocumentIssue + " Rev " + record.DocumentRevision + "    |    الجهة / Authority: " + record.DocumentAuthority, 11, false, XLColor.FromHtml("#DCE6F1"), XLColor.FromHtml("#1F497D"), 20);
        WriteHeaderLine("الحالة / Status: " + record.OrderStatus + "    |    تاريخ التوليد / Generated: " + record.GeneratedAt.ToString("yyyy-MM-dd HH:mm") + " UTC", 11, true, XLColor.FromHtml("#DCE6F1"), XLColor.FromHtml("#1F497D"), 20);
        row++;

        WriteSectionTitle("أ — المناهج والأهداف التدريبية / Lessons & Training Objectives");
        WriteColumnHeaders(new[] { "الدرس / Lesson", "الأهداف / Objectives", "نوع الدليل / Evidence Type", "المحاولات / Attempts", "آخر نتيجة / Last Result", "آخر تقييم / Last Assessment", "المدقق / Examiner", "الحالة / Status", "", "", "", "" });
        foreach (var lesson in record.Lessons)
        {
            foreach (var objective in lesson.Objectives)
            {
                var latest = objective.Attempts.Count > 0 ? objective.Attempts[^1] : null;
                string graderName = "—";
                if (latest?.GraderUserId != null && userNames.TryGetValue(latest.GraderUserId.Value, out string? resolvedGrader))
                    graderName = resolvedGrader;

                WriteDataRow(new[]
                {
                    lesson.LessonCode,
                    objective.ObjectiveCode,
                    objective.EvidenceType,
                    objective.Attempts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    latest?.Result ?? "—",
                    latest?.EvaluatedAt.ToString("yyyy-MM-dd HH:mm") ?? "—",
                    graderName,
                    objective.Status,
                    "", "", "", ""
                }, false);
            }
        }
        row++;

        WriteSectionTitle("ب — فحوصات المراحل / Stage Checks");
        WriteColumnHeaders(new[] { "المرحلة / Stage", "المحاولة / Attempt", "النتيجة / Result", "الإحالة / Remedial Ref", "النقص / Deficiencies", "التقييم / Assessed At", "المدقق / Examiner", "الاعتماد / Approval", "", "", "", "" });
        foreach (var stageCheck in record.StageChecks)
        {
            WriteDataRow(new[]
            {
                stageCheck.StageCode,
                stageCheck.AttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                stageCheck.Result,
                string.IsNullOrWhiteSpace(stageCheck.RemedialReference) ? "—" : stageCheck.RemedialReference,
                string.IsNullOrWhiteSpace(stageCheck.Deficiencies) ? "—" : stageCheck.Deficiencies,
                stageCheck.AssessedAt.ToString("yyyy-MM-dd HH:mm"),
                string.IsNullOrWhiteSpace(stageCheck.ExaminerName) ? "—" : stageCheck.ExaminerName,
                stageCheck.ApprovalRecordId.HasValue ? "معتمد / Approved" : "—",
                "", "", "", ""
            }, true);
        }
        row++;

        WriteSectionTitle("ج — خطط المعالجة / Remedial Plans");
        WriteColumnHeaders(new[] { "الهدف / Objective", "المدرب / Instructor", "الخطة / Remedial Plan", "الاستحقاق / Due", "الحالة / Status", "الملاحظات / Remarks", "", "", "", "", "", "" });
        foreach (var remedialPlan in record.RemedialPlans)
        {
            string remedialObjectiveCode = objectiveCodeById.TryGetValue(remedialPlan.ObjectiveId, out string? resolvedObjectiveCode)
                ? resolvedObjectiveCode
                : remedialPlan.ObjectiveId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string remedialInstructor = "—";
            if (remedialPlan.AssignedInstructorUserId.HasValue && userNames.TryGetValue(remedialPlan.AssignedInstructorUserId.Value, out string? resolvedInstructor))
                remedialInstructor = resolvedInstructor;
            WriteDataRow(new[]
            {
                remedialObjectiveCode,
                remedialInstructor,
                remedialPlan.ArabicPlan + " — " + remedialPlan.EnglishPlan,
                remedialPlan.DueDate?.ToString("yyyy-MM-dd") ?? "—",
                remedialPlan.Status,
                string.IsNullOrWhiteSpace(remedialPlan.CompletionRemarks) ? "—" : remedialPlan.CompletionRemarks,
                "", "", "", "", "", ""
            }, true);
        }
        row++;

        WriteSectionTitle("د — التواقيع الإلكترونية / Electronic Signatures");
        WriteColumnHeaders(new[] { "الدور / Role", "الوصف / Description", "الاسم / Name", "", "", "", "", "", "", "", "", "" });
        foreach (var signature in signatures)
        {
            WriteDataRow(new[] { signature.LabelAr, signature.LabelEn, signature.Name, "", "", "", "", "", "", "", "", "" }, true);
        }
        WriteDataRow(new[] { "المتدرب / Student", "توقيع الاستلام / Acknowledgment", studentSignature, "", "", "", "", "", "", "", "", "" }, true);
        row++;

        worksheet.Range(row, 1, row, totalColumns).Merge();
        var footerCell = worksheet.Cell(row, 1);
        footerCell.Value = "يُصدر هذا السجل آليًا من نظام إدارة التدريب ولا يُعدّل يدويًا. / Issued electronically by the Training Management System; not subject to manual amendment.";
        footerCell.Style.Font.FontSize = 9;
        footerCell.Style.Font.Italic = true;
        footerCell.Style.Font.FontName = "Segoe UI";
        footerCell.Style.Font.FontColor = XLColor.FromHtml("#1F497D");
        footerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        footerCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        footerCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        footerCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1F497D");

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(targetFilePath);
        return targetFilePath;
    }

    private async Task<string> ReadOrderTrackAsync(SqliteConnection connection, int studentId, int trainingOrderId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RegulatoryTrack, StudentId FROM TrainingOrders WHERE Id = @orderId;";
        command.Parameters.AddWithValue("@orderId", trainingOrderId);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Training order was not found.");
        if (reader.GetInt32(1) != studentId) throw new InvalidOperationException("The training order does not belong to this student.");
        return reader.GetString(0);
    }

    private async Task<int> ResolveGoverningVersionAsync(SqliteConnection connection, int studentId, int trainingOrderId, string regulatoryTrack)
    {
        using (var evidenceCommand = connection.CreateCommand())
        {
            evidenceCommand.CommandText = "SELECT CurriculumVersionId FROM ObjectiveProgress WHERE StudentId = @studentId AND TrainingOrderId = @orderId ORDER BY Id LIMIT 1;";
            evidenceCommand.Parameters.AddWithValue("@studentId", studentId);
            evidenceCommand.Parameters.AddWithValue("@orderId", trainingOrderId);
            object? evidenceVersion = await evidenceCommand.ExecuteScalarAsync();
            if (evidenceVersion != null && evidenceVersion != DBNull.Value)
                return Convert.ToInt32(evidenceVersion, CultureInfo.InvariantCulture);
        }
        using (var approvedCommand = connection.CreateCommand())
        {
            approvedCommand.CommandText = @"
                SELECT v.Id FROM CurriculumVersions v
                JOIN CurriculumTemplates t ON t.Id = v.CurriculumTemplateId
                WHERE t.StreamKey = @track AND v.ReviewStatus = 'Approved'
                ORDER BY v.EffectiveFrom DESC, v.Id DESC LIMIT 1;
            ";
            approvedCommand.Parameters.AddWithValue("@track", regulatoryTrack);
            object? approvedVersion = await approvedCommand.ExecuteScalarAsync();
            if (approvedVersion != null && approvedVersion != DBNull.Value)
                return Convert.ToInt32(approvedVersion, CultureInfo.InvariantCulture);
        }
        using (var anyCommand = connection.CreateCommand())
        {
            anyCommand.CommandText = @"
                SELECT v.Id FROM CurriculumVersions v
                JOIN CurriculumTemplates t ON t.Id = v.CurriculumTemplateId
                WHERE t.StreamKey = @track
                ORDER BY v.EffectiveFrom DESC, v.Id DESC LIMIT 1;
            ";
            anyCommand.Parameters.AddWithValue("@track", regulatoryTrack);
            object? anyVersion = await anyCommand.ExecuteScalarAsync();
            if (anyVersion != null && anyVersion != DBNull.Value)
                return Convert.ToInt32(anyVersion, CultureInfo.InvariantCulture);
        }
        throw new InvalidOperationException($"No curriculum version exists for training stream '{regulatoryTrack}'.");
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
