using System;

namespace EAATrainingManager.Models;

public enum ReviewStatus
{
    Draft,
    UnderReview,
    Approved,
    Superseded,
    Expired,
    NeedsRegulatoryReview
}

public sealed class RegulatoryFramework
{
    public int Id { get; set; }
    public string FrameworkCode { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class RegulatoryDocument
{
    public int Id { get; set; }
    public int FrameworkId { get; set; }
    public string Part { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string ApprovingAuthority { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.NeedsRegulatoryReview;
    public int? ApprovalRecordId { get; set; }
}

public sealed class ProgramApproval
{
    public int Id { get; set; }
    public string StreamKey { get; set; } = string.Empty;
    public int CurriculumVersionId { get; set; }
    public int RegulatoryDocumentId { get; set; }
    public string TrainingMethod { get; set; } = string.Empty;
    public string AircraftOrSimulatorScope { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string Limitations { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.NeedsRegulatoryReview;
    public int? ApprovalRecordId { get; set; }
    public int? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public sealed class RequirementRule
{
    public int Id { get; set; }
    public string RuleIdentifier { get; set; } = string.Empty;
    public int RegulatoryDocumentId { get; set; }
    public string ApplicabilityJson { get; set; } = "{}";
    public string ConditionJson { get; set; } = "{}";
    public string EvidenceType { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Draft;
}

public sealed class ComplianceEvidence
{
    public int Id { get; set; }
    public string SourceRecordType { get; set; } = string.Empty;
    public int SourceRecordId { get; set; }
    public int RequirementRuleId { get; set; }
    public int? SignerUserId { get; set; }
    public DateTime EvidenceDate { get; set; }
    public string AttachmentReference { get; set; } = string.Empty;
    public ReviewStatus VerificationStatus { get; set; } = ReviewStatus.NeedsRegulatoryReview;
}

public sealed class RegulatoryException
{
    public int Id { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public int? RequirementRuleId { get; set; }
    public int? ApprovalRecordId { get; set; }
    public int? RegulatoryDocumentId { get; set; }
    public string AuthorityDecisionReference { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.NeedsRegulatoryReview;
}

public sealed class CurriculumTemplate
{
    public int Id { get; set; }
    public string StreamKey { get; set; } = string.Empty;
    public string ArabicName { get; set; } = string.Empty;
    public string EnglishName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class CurriculumVersion
{
    public int Id { get; set; }
    public int CurriculumTemplateId { get; set; }
    public string VersionLabel { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public int? RegulatoryDocumentId { get; set; }
    public int? ProgramApprovalId { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.NeedsRegulatoryReview;
    public string Notes { get; set; } = string.Empty;
    public int? PublishedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int? PublicationApprovalId { get; set; }
}

public sealed class CurriculumLesson
{
    public int Id { get; set; }
    public int CurriculumVersionId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string LessonCode { get; set; } = string.Empty;
    public string ArabicTitle { get; set; } = string.Empty;
    public string EnglishTitle { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
}

public sealed class TrainingObjective
{
    public int Id { get; set; }
    public int CurriculumLessonId { get; set; }
    public string ObjectiveCode { get; set; } = string.Empty;
    public string ArabicDescription { get; set; } = string.Empty;
    public string EnglishDescription { get; set; } = string.Empty;
    public string CompletionStandard { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public string PrerequisiteCodes { get; set; } = string.Empty;
}

public sealed class ObjectiveProgress
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int TrainingOrderId { get; set; }
    public int ObjectiveId { get; set; }
    public int CurriculumVersionId { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public string Result { get; set; } = "Incomplete";
    public int? GraderUserId { get; set; }
    public int? TrainingSessionId { get; set; }
    public int? ApprovalRecordId { get; set; }
    public int? RegulatoryDocumentId { get; set; }
    public DateTime EvaluatedAt { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public ReviewStatus RegulatoryReviewStatus { get; set; } = ReviewStatus.NeedsRegulatoryReview;
    public string RegulatoryRevision { get; set; } = string.Empty;
}

public sealed class SessionObjectiveLink
{
    public int Id { get; set; }
    public int TrainingSessionId { get; set; }
    public int ObjectiveId { get; set; }
    public int CurriculumLessonId { get; set; }
    public DateTime LinkedAt { get; set; }
    public int? LinkedByUserId { get; set; }
}

public sealed class RemedialPlan
{
    public const string StatusOpen = "Open";
    public const string StatusInProgress = "InProgress";
    public const string StatusCompleted = "Completed";
    public const string StatusWaived = "Waived";

    public int Id { get; set; }
    public int ObjectiveProgressId { get; set; }
    public int StudentId { get; set; }
    public int TrainingOrderId { get; set; }
    public int ObjectiveId { get; set; }
    public int CurriculumVersionId { get; set; }
    public string ObjectiveCode { get; set; } = string.Empty;
    public string ArabicPlan { get; set; } = string.Empty;
    public string EnglishPlan { get; set; } = string.Empty;
    public int? AssignedInstructorUserId { get; set; }
    public string Status { get; set; } = StatusOpen;
    public DateOnly? DueDate { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CompletionRemarks { get; set; } = string.Empty;
    public int? ApprovalRecordId { get; set; }
    public ReviewStatus RegulatoryReviewStatus { get; set; } = ReviewStatus.NeedsRegulatoryReview;
    public int? CreatedByUserId { get; set; }
    public string DueDateText => DueDate.HasValue ? DueDate.Value.ToString("yyyy-MM-dd") : string.Empty;
}

public sealed class StageCheck
{
    public const string ResultPass = "Pass";
    public const string ResultFail = "Fail";
    public const string ResultConditional = "Conditional";

    public int Id { get; set; }
    public int StudentId { get; set; }
    public int TrainingOrderId { get; set; }
    public int CurriculumVersionId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public int? ExaminerUserId { get; set; }
    public string ExaminerName { get; set; } = string.Empty;
    public int AttemptNumber { get; set; } = 1;
    public string Result { get; set; } = ResultPass;
    public string Deficiencies { get; set; } = string.Empty;
    public string RemedialReference { get; set; } = string.Empty;
    public DateTime AssessedAt { get; set; }
    public int? TrainingSessionId { get; set; }
    public int? ApprovalRecordId { get; set; }
    public ReviewStatus RegulatoryReviewStatus { get; set; } = ReviewStatus.NeedsRegulatoryReview;
    public int? RegulatoryDocumentId { get; set; }
    public string RegulatoryRevision { get; set; } = string.Empty;
    public int? CreatedByUserId { get; set; }
}

public sealed class RemainingRequirement
{
    public int ObjectiveId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string LessonCode { get; set; } = string.Empty;
    public string ObjectiveCode { get; set; } = string.Empty;
    public string ArabicDescription { get; set; } = string.Empty;
    public string EnglishDescription { get; set; } = string.Empty;
    public string Status { get; set; } = "Remaining";
    public List<string> Blockers { get; set; } = [];
    public string BlockersText => Blockers.Count == 0 ? string.Empty : string.Join(" • ", Blockers);
    public int Attempts { get; set; }
    public string LastResult { get; set; } = string.Empty;
    public bool HasOpenRemedial { get; set; }
}

public sealed class OfficialTrainingObjective
{
    public int ObjectiveId { get; set; }
    public string ObjectiveCode { get; set; } = string.Empty;
    public string ArabicDescription { get; set; } = string.Empty;
    public string EnglishDescription { get; set; } = string.Empty;
    public string CompletionStandard { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public string Status { get; set; } = "Remaining";
    public List<ObjectiveProgress> Attempts { get; set; } = [];
}

public sealed class OfficialTrainingLesson
{
    public int LessonId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string LessonCode { get; set; } = string.Empty;
    public string ArabicTitle { get; set; } = string.Empty;
    public string EnglishTitle { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public List<OfficialTrainingObjective> Objectives { get; set; } = [];
}

public sealed class OfficialTrainingRecord
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public int TrainingOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string ProgramType { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string RegulatoryTrack { get; set; } = string.Empty;
    public int CurriculumVersionId { get; set; }
    public string VersionLabel { get; set; } = string.Empty;
    public ReviewStatus VersionStatus { get; set; } = ReviewStatus.NeedsRegulatoryReview;
    public DateTime VersionEffectiveFrom { get; set; }
    public string FrameworkCode { get; set; } = string.Empty;
    public string DocumentPart { get; set; } = string.Empty;
    public string DocumentIssue { get; set; } = string.Empty;
    public string DocumentRevision { get; set; } = string.Empty;
    public string DocumentAuthority { get; set; } = string.Empty;
    public DateTime? DocumentEffectiveFrom { get; set; }
    public List<OfficialTrainingLesson> Lessons { get; set; } = [];
    public List<StageCheck> StageChecks { get; set; } = [];
    public List<RemedialPlan> RemedialPlans { get; set; } = [];
    public DateTime GeneratedAt { get; set; }
}

public sealed class TrainingHourIssue
{
    public const string CodeHobbsVariance = "HobbsVariance";
    public const string CodeSessionWindow = "SessionWindow";
    public const string CodeSessionStudentMismatch = "SessionStudentMismatch";
    public const string CodeUnlinkedFlightRecord = "UnlinkedFlightRecord";

    public string Code { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class TrainingHourReconciliation
{
    public int StudentId { get; set; }
    public int TrainingOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public double RequiredHours { get; set; }
    public double ScheduledSessionHours { get; set; }
    public double CompletedSessionHours { get; set; }
    public double FlightRecordHours { get; set; }
    public double HobbsRecordedHours { get; set; }
    public double RemainingRequiredHours { get; set; }
    public bool IsReconciled { get; set; }
    public List<TrainingHourIssue> Issues { get; set; } = [];
}
