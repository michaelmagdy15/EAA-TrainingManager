# EAA-TMS SQLite Inventory

This inventory describes the schema initialized by `EAATrainingManager/Services/DatabaseService.cs`. Update it in the same change as any schema or relationship changes.

## Database setup and migrations

- Production database: `%LocalAppData%\EAA_TrainingManager\eaa_training.db`.
- SQLite runs in WAL mode with foreign-key enforcement enabled for each initialization connection.
- Schema creation and additive upgrades execute in one SQLite transaction.
- `PRAGMA user_version` is the schema version marker. Version `1` is the tracked baseline for the schema that previously relied on startup-time column checks; version `2` adds identity, role, permission, location, and session tables plus append-only audit protection; version `3` adds user/session/location attribution to audit events; version `4` adds per-mutation before/after snapshots and entity version numbers; version `5` adds password-reverified approval evidence and one-time approval consumption records; version `6` adds user-to-location assignments; version `7` adds append-only dispatch release records; version `8` adds append-only cancellation/no-show evidence; version `9` adds resource/person availability windows; version `10` adds the notification outbox; version `11` adds versioned curricula, objectives, and controlled regulatory references; version `12` adds append-only stage-check evidence and remediation tracking; version `13` adds `Users.StudentId` so a user account owned by a trainee can acknowledge their own official training record.
- Training-session operations are state-transition controlled: `Scheduled → Confirmed → Released → Airborne → Landed → Completed`, with reasoned `Cancelled`/`NoShow` resolutions before airborne. `Released` requires checklist/weather/FIF evidence; `Completed` requires a linked flight record after landing.
- Migrations add missing columns without dropping or rewriting records, normalize selected legacy airline/ATP orders to the ETP track, add indexes after columns exist, and commit the version marker only after successful migration.
- A database with a `user_version` newer than the application is rejected. Initialization and migration failures are logged with operation, entity, OS user, process session, and correlation ID, then surfaced to the caller.
- Test runs pass a unique temporary database path and configure temporary backup/log/mirror destinations; the production path is not used by the harness.

## Tables and relationships

| Table | Purpose | Foreign keys |
| --- | --- | --- |
| `Students` | Stable person identity and demographics | — |
| `TrainingOrders` | One or more course/program enrolments per student | `StudentId → Students.Id` (`ON DELETE CASCADE`) |
| `Part141Batches` | Approved-program batch records | — |
| `ETPBatches` | Airline/ETP batch records | — |
| `TrainingSessions` | Planned and executed training sessions | `StudentId → Students.Id` (`ON DELETE CASCADE`) |
| `AircraftResources` | Aircraft and other schedulable resource records | — |
| `AuditEvents` | Append-only entity/action audit history with user, session, location, before/after snapshots, and version number | — |
| `ComplianceRecords` | Student compliance evidence and expiry data | `StudentId → Students.Id` (`ON DELETE CASCADE`) |
| `FlightRecords` | Electronic flight activity and resource-hour evidence | `StudentId → Students.Id` (`ON DELETE CASCADE`); `TrainingSessionId → TrainingSessions.Id` (`ON DELETE SET NULL`) |
| `TrainingAssessments` | Assessment, stage-check, deficiency, and remedial evidence | `StudentId → Students.Id` (`ON DELETE CASCADE`) |
| `PersonnelRecords` | Instructor/personnel directory | — |
| `Users` | Local user account, password hash, and enabled/disabled state; `StudentId` (v13) links a trainee-owned account to a student profile for self-acknowledgment of official records | — |
| `Roles` | Configurable role catalog | — |
| `Permissions` | Resource and permission-level catalog | — |
| `UserRoles` | User-to-role assignment with assignment attribution | `UserId → Users.Id` and `RoleId → Roles.Id` (`ON DELETE CASCADE`); `AssignedByUserId → Users.Id` (`ON DELETE SET NULL`) |
| `RolePermissions` | Role-to-permission grants | `RoleId → Roles.Id` and `PermissionId → Permissions.Id` (`ON DELETE CASCADE`) |
| `Locations` | Configurable operating location catalog | — |
| `UserLocations` | Allowed operating locations for each user, with assignment attribution | `UserId → Users.Id` and `LocationId → Locations.Id` (`ON DELETE CASCADE`); `AssignedByUserId → Users.Id` (`ON DELETE SET NULL`) |
| `UserSessions` | Authenticated sessions, expiry, and location | `UserId → Users.Id` (`ON DELETE CASCADE`); `LocationId → Locations.Id` (`ON DELETE SET NULL`) |
| `ApprovalRecords` | Password-verified approval evidence for sensitive transitions | Approver/session references are preserved; entity is polymorphic |
| `ApprovalUses` | One-time consumption of a transition approval | `ApprovalId → ApprovalRecords.Id`, `ConsumedByUserId → Users.Id`, and `SessionId → UserSessions.SessionId` (`ON DELETE RESTRICT`) |
| `DispatchReleases` | Immutable pre-flight dispatch release, checklist, weather briefing, and flight-information-file reference | `TrainingSessionId → TrainingSessions.Id` (`ON DELETE RESTRICT`); user/session/location references preserve nullable attribution |
| `SessionExceptions` | Append-only cancellation/no-show outcome and reason | `TrainingSessionId → TrainingSessions.Id` (`ON DELETE RESTRICT`); user/session/location references preserve nullable attribution |
| `AvailabilityWindows` | Time-bounded availability or blackout windows for students, instructors, resources, rooms, or operator-entered weather restrictions | User/session/location references preserve nullable attribution |
| `NotificationOutbox` | Queued schedule/dispatch events for future delivery adapters | — |
| `RegulatoryFrameworks` | Regulatory framework family and authority catalog | — |
| `RegulatoryDocuments` | Controlled document part/issue/revision/effective dates/source/approver | `FrameworkId → RegulatoryFrameworks.Id` (`ON DELETE RESTRICT`); creator reference to `Users` |
| `CurriculumTemplates` | Named curriculum stream templates using the existing program taxonomy | — |
| `CurriculumVersions` | Effective-dated syllabus versions with controlled-document link and review status | Template/document references are restricted from deletion |
| `ProgramApprovals` | Program/course/method/fleet scope and approval-validity record | Curriculum/document/approval references are restricted from deletion |
| `CurriculumLessons` | Versioned stages and lessons | `CurriculumVersionId → CurriculumVersions.Id` (`ON DELETE RESTRICT`) |
| `TrainingObjectives` | Objective descriptions, completion standards, and evidence types | `CurriculumLessonId → CurriculumLessons.Id` (`ON DELETE RESTRICT`) |
| `ObjectivePrerequisites` | Objective dependency edges | Objective references are restricted from deletion |
| `RequirementRules` | Configurable conditions tied to a regulatory document | `RegulatoryDocumentId → RegulatoryDocuments.Id` (`ON DELETE RESTRICT`) |
| `ComplianceEvidence` | Evidence and signer attached to an explicit requirement rule | Rule reference is restricted from deletion; signer references `Users` |
| `RegulatoryExceptions` | Authority decision, waiver/credit/expiry evidence and review status | Requirement/approval references are restricted from deletion |
| `ObjectiveProgress` | Student/order/version objective attempts and outcomes | Append-only: `trg_objective_progress_no_update`/`_no_delete` reject edits and deletions; student/order/objective/version references are restricted from deletion |

The student/order relationship is intentionally one-to-many. Student deletion is soft-deletion in application workflows; the database cascade is retained for referential integrity if a hard delete is ever performed outside the normal workflow.

## Indexes

| Table | Indexes |
| --- | --- |
| `Students` | `idx_students_norm` (`NormalizedName`) |
| `TrainingOrders` | `idx_orders_student` (`StudentId`); `idx_orders_num_yr` (`OrderNumber`, `Year`); `idx_orders_year` (`AcademicYear`); `idx_orders_track` (`RegulatoryTrack`); `idx_orders_status` (`Status`); `idx_orders_milestone` (`Milestone`) |
| `Part141Batches` | `idx_part141_batch_id` (`BatchId`) |
| `ETPBatches` | `idx_etp_batch_id` (`BatchId`) |
| `TrainingSessions` | `idx_sessions_start` (`StartAt`); `idx_sessions_student` (`StudentId`, `StartAt`); `idx_sessions_instructor` (`InstructorName`, `StartAt`); `idx_sessions_resource` (`ResourceName`, `StartAt`) |
| `AircraftResources` | `idx_resources_status` (`Status`); `idx_resources_base` (`Base`) |
| `AuditEvents` | `idx_audit_entity` (`EntityType`, `EntityId`, `OccurredAt DESC`); `idx_audit_time` (`OccurredAt DESC`) |
| `ComplianceRecords` | `idx_compliance_student` (`StudentId`, `RecordType`); `idx_compliance_expiry` (`ExpiresAt`) |
| `FlightRecords` | `idx_flight_records_student` (`StudentId`, `StartAt`); `idx_flight_records_resource` (`ResourceName`, `StartAt`); `idx_flight_records_session` (`TrainingSessionId`) |
| `TrainingAssessments` | `idx_assessments_student` (`StudentId`, `AssessedAt DESC`); `idx_assessments_result` (`Result`, `AssessedAt DESC`) |
| `PersonnelRecords` | `idx_personnel_role` (`Role`, `IsActive`) |
| `UserRoles` | `idx_user_roles_user` (`UserId`) |
| `RolePermissions` | `idx_role_permissions_permission` (`PermissionId`) |
| `Locations` | `idx_locations_active` (`IsActive`, `DisplayName`) |
| `UserLocations` | `idx_user_locations_location` (`LocationId`, `UserId`) |
| `UserSessions` | `idx_sessions_user` (`UserId`, `StartedAt DESC`); `idx_sessions_expiry` (`ExpiresAt`, `EndedAt`) |
| `ApprovalRecords` | `idx_approval_entity` (`EntityType`, `EntityId`, `Transition`, `ApprovedAt DESC`) |
| `DispatchReleases` | `idx_dispatch_release_time` (`ReleasedAt DESC`) |
| `SessionExceptions` | `idx_session_exceptions_session` (`TrainingSessionId`, `RecordedAt DESC`) |
| `AvailabilityWindows` | `idx_availability_resource_window` (`ResourceType`, `ResourceName`, `StartAt`, `EndAt`, `IsArchived`) |
| `NotificationOutbox` | `idx_notification_status_created` (`Status`, `CreatedAt`); `idx_notification_entity` (`EntityType`, `EntityId`) |
| `RegulatoryDocuments` | `idx_regulatory_documents_framework` (`FrameworkId`, `Part`, `Issue`, `Revision`) |
| `CurriculumVersions` | `idx_curriculum_versions_template` (`CurriculumTemplateId`, `EffectiveFrom DESC`) |
| `ProgramApprovals` | `idx_program_approvals_stream` (`StreamKey`, `ReviewStatus`, `ValidFrom`) |
| `CurriculumLessons` | `idx_curriculum_lessons_version` (`CurriculumVersionId`, `SequenceNumber`) |
| `TrainingObjectives` | `idx_training_objectives_lesson` (`CurriculumLessonId`, `ObjectiveCode`) |
| `ObjectivePrerequisites` | `idx_objective_prerequisites_prereq` (`PrerequisiteObjectiveId`) |
| `RequirementRules` | `idx_requirement_rules_document` (`RegulatoryDocumentId`, `ReviewStatus`) |
| `ComplianceEvidence` | `idx_compliance_evidence_rule` (`RequirementRuleId`, `VerificationStatus`) |
| `RegulatoryExceptions` | `idx_regulatory_exceptions_entity` (`EntityType`, `EntityId`, `ReviewStatus`) |
| `ObjectiveProgress` | `idx_objective_progress_student` (`StudentId`, `TrainingOrderId`, `EvaluatedAt DESC`); `idx_objective_progress_version` (`CurriculumVersionId`, `ObjectiveId`, `Result`) |

Database triggers reject audit/approval/objective-progress update/delete operations and physical user-account deletion; user accounts are disabled and active sessions revoked instead.

## Service dependencies

- `App.DatabaseService` owns initialization and operational reads/writes.
- `ExcelSyncService` imports workbooks and generates official reports from `DatabaseService` data.
- `ExcelMirrorService` schedules a report mirror and writes it to LocalAppData plus the Desktop in production.
- `BackupService` snapshots the initialized SQLite file, validates snapshots, retains the newest 30 snapshots, and restores a validated snapshot after first creating a pre-restore safety snapshot.
- `IdentityService` performs PBKDF2 password verification, bootstrap-admin setup, role/permission checks, location-bound sessions, account disable/revocation, password-reverified approvals, and attributable identity audit events. `LoginWindow` gates access to the operational shell.
- `FlightOperationsService` validates dispatch checklist completeness and coordinates the service-enforced dispatch workflow with `DatabaseService` release persistence and post-flight recording.
- `CurriculumService` manages versioned stream templates, lessons, objectives, controlled-document references, and objective progression. Missing or unapproved sources remain `NeedsRegulatoryReview`.
- Pages, dialogs, and view models call domain/database operations through `DatabaseService`; they must not issue ad-hoc SQL or change schema directly.

## Verification coverage

The console verification harness checks isolated initialization, legacy upgrade/data preservation, failed-migration rollback, snapshot restore/corruption rejection, rotating structured diagnostics, identity bootstrap/login/location assignment, role grants/direct service denials, session revocation, one-use approval consumption, before/after snapshots, immutable audit/user history, availability and weather-window enforcement, reschedule conflict checks, dispatch checklist release, cancellation/no-show reasons, state progression, and duplicate-safe post-flight completion.
