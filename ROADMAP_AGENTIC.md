# EAA-TMS Competitive Roadmap

## Purpose

This roadmap evolves EAA Training Management System from an offline student/order registry into a complete Egyptian aviation training operations platform comparable to leading systems such as Talon ETA. It is tailored to the program streams already present in the application and must be validated against current Egyptian Civil Aviation Authority (ECAA) / Egyptian Civil Aviation Regulations (ECAR) controlled documents before regulatory rules are implemented.

The strategy is deliberately phased. Reliability and data integrity come first; flight operations come next; curriculum, finance, mobile access, and multi-site synchronization follow only after the operational core is trustworthy.

## Product target

EAA-TMS should become the system of record for:

- student identity and enrolment;
- curriculum and training progression;
- aircraft, simulator, instructor, and room resources;
- scheduling, dispatch, and flight execution;
- assessments, electronic records, and approvals;
- billing and student accounts;
- compliance, audit, and reporting;
- offline field operations with controlled synchronization.

## Non-negotiable engineering principles

1. Preserve the current student/order separation. One person may have many orders.
2. Every operational mutation must be auditable and recoverable.
3. No destructive delete for regulated records.
4. Offline operation must be intentional: queue changes, expose sync state, and resolve conflicts explicitly.
5. Domain rules belong in services/domain logic, not page code.
6. Every phase ships with tests, migration coverage, fixtures, and operator documentation.
7. Do not claim a feature is complete until its workflow works from UI to persistence to report/export.
8. Do not replace SQLite or WinUI prematurely. First prove the domain model and workflows.

## Current baseline

The project already contains a .NET 9 WinUI 3 desktop application with SQLite, Excel import/export, Arabic/English localization, backups, soft deletion, demographics, update delivery, orders, students, assessments, flight records, resources, and scheduling-related models/pages.

Before feature expansion, stabilize the existing system. The current verification harness runs 18 suites despite documentation describing 15, and recent execution showed repeated SQLite `attempt to write a readonly database` errors with cascading failures. Treat test determinism and database correctness as a release blocker.

## Egyptian/EAA regulatory and program baseline

This roadmap does not replace ECAA legal, licensing, or approved training-organization documents. The application should implement a configurable compliance matrix whose source is an identified controlled document, issue, revision, effective date, and approving authority. Never hard-code an inferred minimum, license privilege, medical requirement, examination rule, or hour requirement from a web page or from another country's Part 61/141 rules.

The current app's operational taxonomy remains the product baseline:

1. **Part 61 / نظام حر** — modular or individually managed training orders, including PPL, CPL/IR, IR, CPL, and related progression records already supported by the smart order form.
2. **Part 141 / نظام معتمد** — approved structured programs and batches, with batch number, intake, program, trainee roster, progress, completion, and official evidence.
3. **ETP / خط جوي** — airline/air-transport pilot pathway and ATP/ATPL-theory-related records already represented by ETP batches and the ATP navigation stream. The exact ECAA-approved scope must be configured by the responsible training authority.
4. **Type Rating & Hour Building / تجديد طراز وفرق وبناء ساعات** — aircraft/type activity, fleet, simulator or flight hours, recurrent/renewal activity, and supporting records. A type-rating record must not be treated as a license or approval record.
5. **Evaluation & Equivalency / تقييم ومعادلات** — foreign-license conversion, level assessment, ATP/PPL/IR/CPL equivalency, gap analysis, documents, authority correspondence, and resulting approved training requirements.

The system should support EAA's actual operating locations and organizational boundaries, including Misr Flying College, October Airport, El-Tor, and Asyut where applicable. Location support must be configurable and must not assume every program, aircraft, instructor, or approval is available at every location.

### Required compliance data model

Add a regulatory reference layer rather than embedding compliance claims in code:

- `RegulatoryFramework`: ECAR/ECAA reference family and authority.
- `RegulatoryDocument`: part, issue, revision, effective date, source file, and superseded status.
- `ApprovalRecord`: academy/ATO approval, scope, location, fleet, course, validity, limitations, and evidence.
- `ProgramApproval`: approved program/stream, curriculum version, training method, aircraft/simulator scope, and approval reference.
- `RequirementRule`: rule identifier, applicability, threshold/condition, evidence type, and effective period.
- `ComplianceEvidence`: source record, signer, date, attachment, verification state, and audit history.
- `RegulatoryException`: waiver, credit, exemption, authority decision, expiry, and approval evidence.

Every automated compliance result should display the rule ID, source document and revision, effective date, evaluated facts, result, and reviewer/override history. If the source is missing or ambiguous, the result must be `Needs regulatory review`, not `Compliant`.

The public ECAA legislation index identifies Pilot Schools under Part 141 and publishes issue/revision metadata; use the official ECAA controlled document and EAA approved manuals as the authoritative implementation inputs. EAA's public training catalogue lists PPL, CPL, IR, integrated PPL-IR-CPL, and ATP-related training, consistent with the current app's aviation-program direction. [ECAA legislation index](https://civilaviation.gov.eg/ECAA/Legisation), [ICAO EAA training profile](https://igat.icao.int/ated/TrainingCatalogue/Profile/98), [EAA course catalogue](https://www.eaaegypt.com/courses.pdf)

## Delivery sequence

| Phase | Outcome | Gate |
|---|---|---|
| 0 | Baseline reliability and architecture control | All critical tests deterministic |
| 1 | Production-grade identity, security, audit, and permissions | Every sensitive action attributable |
| 2 | Flight Operations MVP | A flight can be planned, dispatched, completed, and audited |
| 3 | Curriculum and electronic training records | Training progress is objective- and syllabus-driven |
| 4 | Fleet, simulator, and maintenance operations | Resources are schedulable and airworthiness-aware |
| 5 | Billing, cashier, and commercial controls | Training activity reconciles with money |
| 6 | Mobile and field workflows | Students, instructors, and dispatch work away from desks |
| 7 | Hybrid synchronization and multi-site deployment | Multiple locations operate safely from one system of record |
| 8 | Intelligence and commercialization | Forecasting, benchmarking, APIs, and deployable product packaging |

---

## Phase 0 — Baseline reliability and architecture control

### Objective

Make the current product safe to extend and establish a trustworthy definition of done.

### Workstreams

- Reproduce and fix the SQLite read-only failure in a clean test database.
- Separate production database paths from test database paths.
- Ensure tests never mutate the user's real LocalAppData database.
- Add deterministic database reset and fixture creation.
- Inventory every table, foreign key, index, migration, and service dependency.
- Replace silent `catch {}` blocks in core services with structured diagnostics.
- Add a durable application log with rotation and bilingual user-safe error messages.
- Reconcile version strings across project files, README, manifests, installers, and update metadata.
- Rename or update the test documentation from 15 to the actual suite count.
- Add CI build/test/package checks.

### Acceptance criteria

- A clean machine can initialize the database and run the entire test suite repeatedly.
- Tests pass three consecutive times without depending on prior local state.
- Production and test databases are physically distinct.
- Failed writes include operation, entity, user/session, and correlation ID in logs.
- Database migrations can upgrade a representative v2.2.x database without data loss.
- Release build, standalone executable, installer, and updater metadata agree on version.

### Exit gate

No Phase 1 feature begins until database setup, migrations, backups, and test isolation are reliable.

---

## Phase 1 — Identity, security, audit, and permissions

### Objective

Turn a single-user desktop tool into a controlled operational system.

### Workstreams

- Add users, roles, permissions, locations, and active sessions.
- Define roles: administrator, training manager, dispatch, instructor, examiner, finance, maintenance, student, auditor.
- Add page/action-level permissions: none, read-only, limited edit, full edit, approve.
- Add immutable audit events for create, edit, archive, restore, approve, dispatch, complete, export, login, and update operations.
- Add electronic approval/signature records for regulated transitions.
- Add record version history and before/after snapshots for critical entities.
- Encrypt secrets and tokens; never store update credentials in source or plain logs.
- Add configurable academic year, location, and department scoping.
- Add regulatory-document access control so only designated EAA/ECAA compliance users can publish or retire rules, approvals, waivers, and evidence.

### Acceptance criteria

- An unauthorized user cannot view or mutate restricted records.
- Every critical mutation shows who, when, where, what changed, and why.
- Audit records cannot be edited through the application.
- Administrative exports can be filtered by date, user, action, and entity.
- A manager can disable a user without deleting historical attribution.

### Exit gate

Role and audit behavior is covered by automated tests and demonstrated in a permission matrix.

---

## Phase 2 — Flight Operations MVP

### Objective

Make the system useful during daily dispatch operations, not only after flights are recorded.

### Core domain objects

- AvailabilityWindow
- Booking
- FlightSchedule
- DispatchRelease
- FlightInformationFile
- Cancellation
- NoShow
- WeatherRestriction
- InstructorAvailability
- AircraftAvailability
- SimulatorBooking

### Workstreams

- Day/week calendar views for students, instructors, aircraft, simulators, and rooms.
- Conflict detection across people, aircraft, locations, and time windows.
- Availability and blackout management.
- Drag-and-drop rescheduling with permission checks and audit events.
- Dispatch board with planned, confirmed, released, airborne, landed, completed, cancelled, and no-show states.
- Pre-flight checklist and dispatch release.
- Post-flight debrief and completion workflow.
- Automatic notification queue for schedule changes.
- Operational dashboards: utilization, cancellation reasons, no-shows, delays, and throughput.

### Acceptance criteria

- A dispatcher can create a booking only when all required resources are available.
- The system prevents double-booking an instructor, aircraft, simulator, or student.
- A schedule change records the reason and notifies affected users.
- A released flight cannot be silently edited.
- A completed flight creates the correct training record and resource-hour updates.
- Every state transition is auditable and reversible where legally appropriate.

### Exit gate

Pilot the workflow with one real training stream and one aircraft fleet before general rollout.

---

## Phase 3 — Curriculum and electronic training records

### Objective

Replace course-level status tracking with objective-level training progression.

### Workstreams

- Versioned curriculum templates for Part 61, Part 141, ETP, type rating, and evaluation.
- Course, stage, lesson, objective, prerequisite, and completion-standard entities.
- Syllabus version effective dates and historical preservation.
- Instructor grading: satisfactory, unsatisfactory, incomplete, remedial, waived.
- Stage checks, checkrides, examiner assignment, attempts, deficiencies, and remedial plans.
- Student progress dashboard with remaining requirements and blockers.
- Electronic signatures for instructor, examiner, student, and manager.
- Read-only official training record and PDF export.
- Link every curriculum version, lesson requirement, stage check, completion decision, and waiver to the applicable ECAA/ECAR approval reference and EAA approved-training-document revision.
- Support regulatory review status: draft, under review, approved, superseded, expired, and needs clarification.

### Acceptance criteria

- Managers can publish a new syllabus version without changing historical records.
- A student cannot progress when required prerequisites are incomplete unless an authorized waiver exists.
- Training-hour totals reconcile between flight records, lessons, and course requirements.
- An auditor can reconstruct the student's complete trajectory and evidence.
- Failed objectives produce a visible remedial workflow.

### Exit gate

One complete curriculum is implemented end-to-end, including lesson scheduling, grading, stage check, graduation, and export.

---

## Phase 4 — Fleet, simulator, and maintenance operations

### Objective

Make aircraft and simulator capacity, safety, and maintenance first-class operational concepts.

### Workstreams

- Aircraft and simulator master data with registration, type, base, status, and capabilities.
- Tach/Hobbs/cycles and utilization tracking.
- Scheduled inspections based on dates, hours, cycles, and calendar limits.
- Maintenance events, defects, grounding, return-to-service, and deferred defects.
- Resource capability rules for curriculum lessons.
- Parts, inventory, vendors, purchase orders, and minimum stock alerts.
- Maintenance dashboard and due-soon alerts.
- Optional integration boundary for external maintenance systems.

### Acceptance criteria

- Grounded aircraft cannot be scheduled or dispatched.
- A flight updates the correct resource counters exactly once.
- Inspection due status is visible before dispatch.
- Return-to-service requires an authorized user and evidence.
- Resource utilization and downtime reports reconcile with completed flights.

### Exit gate

No scheduling or dispatch path can bypass aircraft capability or airworthiness rules.

---

## Phase 5 — Billing, cashier, and commercial controls

### Objective

Connect training activity to student accounts and financial control.

### Workstreams

- Rate cards for aircraft, instructor, simulator, exams, and course packages.
- Student account ledger with charges, payments, credits, refunds, and adjustments.
- Invoices, receipts, deposits, and payment status.
- Cashier permissions and end-of-day reconciliation.
- Balance restrictions on scheduling or dispatch, configurable by management.
- Discounts, scholarships, sponsored trainees, and government billing.
- Accounting export/API boundary.
- Financial reports by student, course, fleet, instructor, branch, and date.

### Acceptance criteria

- Every charge is traceable to a training event or approved manual adjustment.
- A payment cannot be deleted; corrections are compensating entries.
- Daily cashier totals reconcile.
- Finance users cannot alter training evidence.
- Managers can configure whether an unpaid balance blocks booking, dispatch, or graduation.

### Exit gate

Finance can operate a complete billing cycle without manipulating the operational database directly.

---

## Phase 6 — Mobile and field workflows

### Objective

Move the right workflows from the office desktop to the flight line.

### Student mobile capabilities

- Schedule and change notifications.
- Availability submission.
- Course progress and outstanding requirements.
- Balance and payment history.
- Documents and messages.
- Confirmation, cancellation request, and acknowledgement.

### Instructor mobile capabilities

- Daily roster and schedule.
- Student profile and prerequisites.
- Lesson objectives and grading.
- Flight record entry.
- Defect/report submission.
- Electronic signature.

### Dispatch mobile capabilities

- Live dispatch board.
- Aircraft status.
- Weather/operational notices.
- Release and return workflows.
- Offline queue with sync status.

### Acceptance criteria

- Critical field workflows work with intermittent connectivity.
- Offline changes show pending/synced/conflicted states.
- No duplicate flight records are created during retries.
- Notifications are delivered only to authorized recipients.

### Exit gate

One real dispatch shift can be completed without requiring the desktop for normal actions.

---

## Phase 7 — Hybrid synchronization and multi-site deployment

### Objective

Support multiple airfields or academy locations while preserving offline resilience.

### Workstreams

- Central API and synchronization service.
- Local operational cache and outbound change queue.
- Stable IDs and idempotency keys for all syncable mutations.
- Conflict policy by entity type.
- Location ownership and cross-location visibility.
- Central identity and role management.
- Central reporting warehouse/read model.
- Backup, restore, disaster recovery, and sync monitoring.
- Admin tools for quarantined or conflicting records.

### Acceptance criteria

- A location can continue essential operations while disconnected.
- Reconnection does not duplicate or silently overwrite records.
- Conflicts are visible, explainable, and resolvable by authorized users.
- Central reports identify data freshness and sync health.
- Restore procedures are tested, timed, and documented.

### Exit gate

Two isolated test locations can operate simultaneously, disconnect, reconnect, and reconcile successfully.

---

## Phase 8 — Intelligence, integrations, and commercialization

### Objective

Create competitive differentiation and a maintainable product platform.

### Workstreams

- Demand and capacity forecasting.
- Student-at-risk detection based on delays, failed objectives, cancellations, and inactivity.
- Instructor and fleet utilization optimization.
- Training bottleneck analysis.
- Management KPI dashboards.
- Public, versioned REST API and webhooks.
- Integration adapters for accounting, weather, flight tracking, maintenance, identity, and messaging.
- Tenant configuration and white-label settings.
- Automated onboarding, import validation, and data-quality scoring.
- Security review, penetration testing, privacy policy, retention rules, and incident response.

### Acceptance criteria

- Every AI recommendation displays its source data and confidence, and never silently changes regulated records.
- Integrations retry safely and expose failure state.
- APIs are permissioned, rate-limited, logged, and versioned.
- A new academy can configure streams, curricula, roles, rates, and locations without code changes.

## Cross-phase quality gates

Every phase must include:

- database migration and rollback/restore test;
- unit tests for domain rules;
- integration tests for service-to-database behavior;
- UI tests for the primary operator workflow;
- Arabic and English localization checks;
- RTL and LTR layout checks;
- offline/interrupted-operation test where relevant;
- audit-log verification;
- performance baseline;
- security and authorization test;
- operator documentation and release notes.

## Suggested release milestones

- **R0 Stabilized 2.3:** reliable database, tests, migrations, diagnostics, and release pipeline.
- **R1 Operations 3.0:** scheduling, conflict detection, dispatch, flight completion, and notifications.
- **R2 Training Records 3.5:** curriculum, objectives, stage checks, electronic signatures, and official records.
- **R3 Fleet and Finance 4.0:** maintenance, aircraft utilization, billing, cashier, and accounting export.
- **R4 Field Platform 5.0:** mobile apps, offline queues, central API, and multi-site synchronization.
- **R5 Intelligent Platform 6.0:** analytics, forecasting, integrations, and configurable deployments.

## Master AI agent prompt

Copy this prompt into an AI coding agent at the beginning of each phase, then append the phase-specific prompt below.

```text
You are the lead engineer for EAA Training Management System, a .NET 9 C# WinUI 3 aviation training management application.

Repository context:
- Existing UI: WinUI 3 desktop application.
- Existing persistence: SQLite under LocalAppData for production.
- Existing domains: students, training orders, Excel import/export, backups, demographics, assessments, resources, flight records, training sessions, updates, and bilingual Arabic/English UI.
- Existing architecture folders: Models, Services, ViewModels, Views, Dialogs, Helpers.
- Existing verification project: EAATrainingManager.Tests.

Your operating rules:
1. Inspect the repository before editing. Read relevant models, services, views, tests, README, and MASTER.md.
2. Preserve existing user data and backward compatibility.
3. Never use destructive schema changes. Add migrations and test them.
4. Keep student identity separate from training orders.
5. Put business rules in testable services/domain logic, not code-behind.
6. Preserve Arabic/English behavior, RTL/LTR layout, offline operation, soft deletion, backups, and Excel compatibility.
7. Do not hide exceptions. Add structured logs and user-safe error handling.
8. Do not introduce a cloud dependency unless the phase explicitly requires it.
9. Do not claim completion from compilation alone. Verify persistence, UI behavior, permissions, and recovery paths.
10. Do not rewrite unrelated code or remove existing features to make tests pass.

Execution protocol:
A. Establish a baseline: git status, project build, test run, database location, and current failures.
B. Write a short implementation plan with files, schema changes, risks, and acceptance tests.
C. Implement in small vertical slices: model/schema, service, view model, UI, tests, documentation.
D. After each slice, run focused tests and inspect generated data/output.
E. Run the full verification suite and build the application in the target configuration.
F. Review for data loss, concurrency, authorization, localization, and offline failure modes.
G. Report: files changed, schema migrations, tests run, results, known limitations, and recommended next step.

Definition of done:
- Feature works through the real UI workflow.
- Data survives restart and migration.
- Failure and retry behavior are defined.
- Audit behavior exists for regulated mutations.
- Arabic and English labels are complete.
- Tests cover happy path, invalid input, duplicate/retry, permission denial, and recovery.
- Documentation and release notes are updated.
```

## Phase-specific AI prompts

### Phase 0 prompt

```text
Execute Phase 0: Baseline reliability and architecture control.

First reproduce the current SQLite readonly failure using a clean isolated test database. Identify the exact path, file permissions, connection lifecycle, WAL/shm behavior, and any accidental reuse of the production database. Fix the root cause rather than weakening assertions.

Then inventory schema creation and migrations, isolate all test data, add deterministic fixtures, improve structured logging, reconcile the actual test count, and add build/test/package validation. Do not begin new product features.

Acceptance tests:
- full test suite passes three consecutive times;
- test execution never modifies production LocalAppData;
- migration test upgrades an older representative database;
- backup and restore are both verified;
- Release build succeeds;
- failure logs identify operation and correlation ID.
```

### Regulatory configuration prompt

```text
Before implementing any ECAR/ECAA compliance automation, inspect the repository's current Part 61, Part 141, ETP, Type Rating/Hour Building, and Evaluation/Equivalency terminology. Preserve those exact program streams and their Arabic labels.

Create a regulatory reference layer for EAA. Do not invent regulatory minimums from memory, search results, FAA rules, EASA rules, or another academy's syllabus. Require each rule to reference an ECAA/ECAR controlled document, part, issue, revision, effective date, applicability, and evidence requirement. Add statuses Draft, UnderReview, Approved, Superseded, Expired, and NeedsRegulatoryReview.

Implement the data model and review workflow first. A missing, ambiguous, or expired source must produce NeedsRegulatoryReview and must not be reported as compliant. Every override requires an authorized reviewer, reason, evidence, and audit event.

Map the existing EAA streams without changing their meaning:
- Part 61 / نظام حر: PPL, CPL/IR, IR, CPL and modular orders;
- Part 141 / نظام معتمد: approved programs and batches;
- ETP / خط جوي: ATP/ATPL-theory-related and airline pathway records;
- Type Rating & Hour Building / تجديد طراز وفرق وبناء ساعات;
- Evaluation & Equivalency / تقييم ومعادلات: ATP, PPL, IR/CPL and foreign-license/level assessment.

Add tests proving historical records retain the rule revision that was effective when the training decision occurred, even after a new revision is published.
```

### Phase 1 prompt

```text
Execute Phase 1: Identity, security, audit, and permissions.

Design a permission matrix before coding. Implement users, roles, permissions, locations, sessions, audit events, approval records, and critical-record version history. Enforce permissions in services as well as UI. Add tests proving unauthorized users cannot bypass controls through direct service calls.

Use secure secret storage appropriate for Windows. Do not store bearer tokens, passwords, or private keys in source, SQLite, logs, or update manifests.
```

### Phase 2 prompt

```text
Execute Phase 2: Flight Operations MVP.

Design the scheduling and dispatch state machine first. Implement availability windows, bookings, resource conflict detection, dispatch release, flight information files, cancellations, no-shows, post-flight completion, and notification events. Integrate with existing Student, TrainingOrder, TrainingSession, FlightRecord, AircraftResource, and PersonnelRecord models without duplicating entities.

Create tests for overlapping bookings, retries, time zones, daylight-saving boundaries, grounded aircraft, instructor absence, cancellation, rescheduling, and duplicate completion.
```

### Phase 3 prompt

```text
Execute Phase 3: Curriculum and electronic training records.

Build versioned curriculum templates with course, stage, lesson, objective, prerequisite, completion standard, stage check, deficiency, remedial plan, waiver, and electronic signature concepts. Preserve historical syllabus versions. Connect scheduled flights and assessments to objectives and compute remaining requirements.

Prove one complete curriculum end-to-end: enrolment, scheduling, lesson execution, grading, failed objective, remedial training, stage check, completion, graduation, and official export.
```

### Phase 4 prompt

```text
Execute Phase 4: Fleet, simulator, and maintenance operations.

Extend aircraft resources into an airworthiness-aware fleet domain. Implement aircraft capability, status, tach/Hobbs/cycles, inspections, defects, grounding, return-to-service, simulator capacity, parts, inventory, and purchase-order foundations. Enforce resource eligibility and airworthiness in scheduling and dispatch services.

Every counter update must be idempotent and auditable. Test that grounded or capability-incompatible resources cannot be scheduled.
```

### Phase 5 prompt

```text
Execute Phase 5: Billing, cashier, and commercial controls.

Implement rate cards, student ledgers, charges, payments, credits, refunds, adjustments, invoices, receipts, cashier reconciliation, sponsored trainees, and configurable balance restrictions. Use append-only financial entries for corrections. Keep finance permissions separate from training evidence permissions.

Reconcile at least one complete flight-to-charge-to-payment-to-receipt workflow and add tests for duplicate payment submission, refunds, rounding, and end-of-day totals.
```

### Phase 6 prompt

```text
Execute Phase 6: Mobile and field workflows.

Before choosing a mobile technology, document which workflows require native mobile, responsive web, or shared service APIs. Implement the central API contract, authentication, notification events, offline command queue, idempotency keys, sync status, and conflict presentation. Deliver student, instructor, and dispatch workflows incrementally.

Test intermittent connectivity, retries, expired sessions, duplicate submissions, unauthorized access, and delayed notifications.
```

### Phase 7 prompt

```text
Execute Phase 7: Hybrid synchronization and multi-site deployment.

Design synchronization by entity type, ownership, conflict policy, and event ordering before implementation. Add stable IDs, change tracking, idempotent commands, outbound queues, inbound reconciliation, conflict quarantine, location scoping, central reporting, and disaster recovery.

Run a two-location simulation with disconnect/reconnect, concurrent edits, duplicate delivery, clock skew, and partial failure. No record may be silently lost or overwritten.
```

### Phase 8 prompt

```text
Execute Phase 8: Intelligence, integrations, and commercialization.

Implement read-only analytics and recommendations first. Every AI output must show source records, timestamp, confidence, and explanation. AI must never silently alter regulated records, financial entries, training completion, airworthiness, or permissions.

Add versioned APIs, webhooks, integration retry/dead-letter handling, tenant configuration, onboarding validation, security review artifacts, and product-level observability.
```

## First three agent runs

1. Run the Phase 0 prompt and do not proceed until the database/test isolation issue is fixed.
2. Run the Phase 2 prompt for one training stream and one aircraft fleet as a vertical pilot.
3. Run the Phase 3 prompt against the same pilot stream so scheduling, flight execution, assessment, and graduation form one demonstrable product loop.

