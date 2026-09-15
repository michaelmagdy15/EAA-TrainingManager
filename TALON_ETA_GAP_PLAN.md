# EAA Training Manager — Talon ETA Benchmark and Execution Plan

Status: Ready for execution

This document turns the Talon ETA benchmark into an implementation backlog for the Egyptian Aviation Academy Training Manager. It is intentionally written as a product plan, not as a promise to copy Talon’s product or interface.

## 1. Product target

EAA-TMS should become the academy’s operational system of record from admission through graduation and airline/route qualification:

`Applicant → Student dossier → Program/batch → Curriculum → Schedule → Dispatch → Flight/lesson record → Assessment → Compliance → Billing → Graduation → Reporting`

The product should beat a generic commercial flight-school system in the areas that matter to EAA:

- Arabic-first RTL workflows with reliable Arabic/Latin aviation-code rendering.
- Part 61, Part 141, and ETP/الخط جوي as truly separate operational modules.
- ECAA/Ministry-specific evidence, approvals, annexes, and exports.
- Offline-first operation at airfield locations, with controlled LAN synchronization later.
- Egyptian and foreign trainee demographics, consular rosters, and official reporting.
- Auditability and data ownership without requiring a cloud subscription.

## 2. Benchmark summary

Talon ETA describes itself as a web-based system covering curriculum, student records, flight operations, and scheduling. Its published capabilities include smart scheduling, live student progress, course/syllabus control, dispatch and flight tracking, automated billing, electronic recordkeeping, configurable definitions, role/page permissions, aircraft and simulator resource tracking, personnel management, instructor performance, cashier functions, and nearly 300 reports. Source: [Talon ETA official product page](https://www.talonsystems.com/taloneta).

### Capability comparison

| Capability | Current EAA-TMS position | Target position | Priority |
|---|---|---|---|
| Student identity and 360° dossier | Strong foundation: canonical students, deduplication, trajectory | Add documents, contacts, medical, license, consent, status history | P0 |
| Training orders | Implemented foundation with Part 61/141/ETP tracks | Turn orders into enrollments, approvals, lifecycle and audit records | P0 |
| Part 141 cohorts | Models/services and new overview are present | Full cohort administration, curriculum, roster, annex versioning | P0 |
| ETP / route flying | Separate model/service and route overview are present | Operational route assignments, flights, checks, completion evidence | P0 |
| Curriculum and syllabus | Program names and hours only | Versioned courses, stages, lessons, objectives, prerequisites, grading | P0 |
| Scheduling | Not present as a real scheduling engine | Conflict-aware calendar for students, instructors, aircraft, simulators | P0 |
| Dispatch and flight tracking | Not present; type-rating view is not a dispatch ledger | Dispatch board, check-in/out, Hobbs/Tach, fuel, defects, overdue alerts | P0 |
| Aircraft/fleet resources | Not present as a fleet domain | Aircraft, simulator, status, utilization, maintenance thresholds | P0 |
| Instructor/personnel | Not present as a first-class domain | Qualifications, currency, duty limits, leave, workload, performance | P1 |
| Assessments and progression | Completion date and basic milestones | Lesson grades, stage checks, failed items, remedial actions, gates | P0 |
| Compliance | Demographics and planned medical/ELP roadmap | Medical, ELP, documents, expiry rules, solo/checkride gates | P0 |
| Billing/cashier | Not present | Account ledger, fees, deposits, invoices, payments, balances | P1 |
| Reports | Excel sync/export exists; no report builder | Operational dashboards, official templates, scheduled exports, audit reports | P0 |
| Security and authorization | Local app with no real user/role model | Named users, roles, page/action permissions, approvals, audit log | P0 |
| Configurability | Hard-coded fields and labels in many places | Admin-managed definitions, picklists, thresholds, locations | P1 |
| Mobile/field use | Windows desktop only | Responsive web/LAN or companion field client, after offline core | P2 |
| Multi-location operation | Not implemented | Bases, classrooms, airports, fleets, location-specific rules | P1 |
| Integration | Excel mirror/import | Controlled imports, exports, printer/PDF, optional ECAA/ERP integration | P1 |

## 3. Non-negotiable product principles

1. Preserve offline operation. No feature may make the airfield workflow dependent on internet access.
2. Never destroy historical evidence. Use append-only events or versioned records for approvals, grades, dispatches, and corrections.
3. Separate human identity from training activity. A student can have many enrollments, courses, flights, assessments, and orders without inflating headcount.
4. Separate training domains. Part 141 cohort data must never be inferred from ETP route data, and ETP must never be represented as a Part 141 batch.
5. Make compliance visible before an unsafe action. The system should block or warn on expired medicals, missing prerequisites, instructor currency, aircraft unserviceability, and incomplete approvals.
6. Design Arabic and English together. Do not add English as a translation pass after an Arabic-only schema or UI is built.
7. Make every important number explainable. Every KPI must drill down to the records that produced it.
8. Prefer configuration over code for academy-specific labels, statuses, thresholds, document types, and report layouts.

## 4. Recommended architecture direction

### Near term: harden the current desktop product

Keep WinUI 3 and SQLite for the next release. Refactor the current `DatabaseService` into bounded services/repositories so scheduling, compliance, dispatch, billing, and reporting do not become one large service. Introduce migrations, transactions, domain validation, audit events, and testable application services.

Suggested boundaries:

- `Identity`: students, contacts, nationality, documents, consent.
- `Training`: programs, syllabus versions, enrollments, cohorts, lessons, assessments.
- `Orders`: official training orders, annexes, approvals, amendments.
- `Operations`: schedule, dispatch, flights, simulator sessions, defects.
- `Resources`: aircraft, simulators, instructors, rooms, bases, maintenance status.
- `Compliance`: medical, ELP, licenses, currency, expiry and gates.
- `Finance`: fee plans, ledger, invoices, payments, refunds.
- `Reporting`: saved reports, exports, snapshots, delivery queue.
- `Security`: users, roles, permissions, audit log.
- `Sync`: Excel import/export now; LAN replication later.

### Medium term: local multi-terminal operation

Do not jump directly to a cloud rewrite. First define a sync-safe event model and a conflict policy. Then support a local academy server or LAN host with desktop clients, while preserving a read-only/offline fallback at each station.

### Long term: field/mobile surfaces

Add a mobile or browser client only after the domain and sync model are stable. The field surface should focus on instructor actions: view roster, check compliance, start/complete lesson, record grades/hours, sign, and submit.

## 5. Execution backlog

Priority definitions:

- **P0**: required for a credible operational replacement and safety/compliance.
- **P1**: major efficiency, management, or financial capability.
- **P2**: scale, convenience, integrations, and expansion.

Each task should become one branch/PR or one clearly bounded migration. Do not combine schema, UI, and unrelated reporting work into one large change.

### EPIC A — Baseline, quality, and domain foundation (P0)

#### EAA-001 — Establish a clean build/test baseline

Tasks:

- Install/pin the supported .NET 9 SDK in the development environment.
- Make the existing test harness runnable in CI and locally.
- Add a Release build check for x64 WinUI packaging.
- Record the current database schema and migration version.
- Add a smoke test that initializes a blank database and opens the main navigation.

Acceptance criteria:

- A clean checkout builds without relying on generated binaries.
- Tests run from a documented command and fail the build on error.
- A blank database can be created and upgraded from every prior migration.

#### EAA-002 — Introduce explicit database migrations

Tasks:

- Replace scattered `CREATE TABLE IF NOT EXISTS` and ad hoc column checks with numbered migrations.
- Add a `SchemaMigrations` table.
- Make migrations transactional and idempotent.
- Add migration tests for an empty database and a representative v2.x database.
- Add a backup-before-migration step and a clear recovery message.

Acceptance criteria:

- Every schema change has a version and forward migration.
- Failed migrations leave a recoverable backup.
- The app refuses to operate on an unknown future schema instead of silently changing it.

#### EAA-003 — Split persistence from UI/application logic

Tasks:

- Extract interfaces and services from `DatabaseService` by domain.
- Keep SQL in repositories or query classes.
- Add transaction helpers and cancellation tokens.
- Centralize date, nationality, status, and enum conversion.
- Keep the existing UI behavior unchanged during the refactor.

Acceptance criteria:

- New domain services can be unit-tested without WinUI.
- A single operation can atomically write its related records.
- No new feature requires adding unrelated SQL to the central service.

#### EAA-004 — Add domain IDs, status histories, and audit events

Tasks:

- Use stable IDs for students, enrollments, batches, lessons, flights, resources, documents, and orders.
- Add `CreatedBy`, `UpdatedBy`, `CreatedAt`, `UpdatedAt`, and row version fields where appropriate.
- Add append-only audit events for create, edit, archive, restore, approve, reject, complete, and override.
- Record old/new values for sensitive or regulatory fields.

Acceptance criteria:

- An auditor can answer who changed a record, when, and why.
- Corrections do not erase the original approved value.
- Audit records cannot be edited through normal application actions.

### EPIC B — Identity, student dossier, and documents (P0)

#### EAA-010 — Upgrade the student 360° dossier

Tasks:

- Add separate legal name, preferred/display name, Arabic name, date of birth, gender where required, national ID/passport, phone, email, address, emergency contact, and sponsor/employer.
- Add document records instead of putting document references in free text.
- Add student lifecycle statuses: applicant, active, suspended, withdrawn, graduated, archived.
- Preserve current nationality standardization and allow manual correction with an audit reason.

Acceptance criteria:

- Duplicate detection uses name plus identity documents, not name alone.
- A student page shows identity, enrollments, orders, progress, compliance, finance, documents, and history.
- Sensitive fields are permission-controlled and masked where appropriate.

#### EAA-011 — Implement secure attachment/document storage

Tasks:

- Add a document table with type, owner, version, checksum, filename, MIME type, storage path/blob, issue date, expiry date, and approval status.
- Add document categories: training order, annex, national ID, passport, visa, medical, license, ELP, assessment, payment receipt.
- Store files outside the database with transactional metadata and checksum validation, or use a documented blob strategy.
- Add preview/download permissions and archive retention rules.

Acceptance criteria:

- Part 141 annexes are linked to a batch/order and are individually searchable.
- Replacing a document creates a new version and retains the old one.
- Missing or corrupted files are detected by a verification action.

### EPIC C — Programs, syllabi, Part 141, and ETP (P0)

#### EAA-020 — Build versioned curriculum management

Tasks:

- Add program, syllabus version, stage, phase, lesson, objective, prerequisite, minimum hours, grading scale, and required resource types.
- Support effective dates so a new syllabus does not rewrite historical training.
- Add course templates for Part 61, Part 141, and ETP.
- Add lesson completion rules and stage gates.

Acceptance criteria:

- A program can be copied/versioned without changing completed historical records.
- The system can calculate required, completed, remaining, and overdue items.
- Prerequisites are visible and enforced at scheduling/dispatch time.

#### EAA-021 — Finish Part 141 cohort administration

Tasks:

- Promote `Part141Batch` to a full cohort aggregate linked to a program/syllabus version.
- Add intake capacity, official order number/date, approval authority, annex versions, instructor group, base, and cohort status.
- Add roster add/remove/transfer operations with effective dates and reasons.
- Add cohort progress by student, stage, hours, completion, withdrawals, and nationality.
- Add cohort-level official reports and annex download.

Acceptance criteria:

- Part 141 screens never query ETP records.
- A cohort has one authoritative roster and a traceable membership history.
- Total enrolled, active, completed, withdrawn, local, and foreign counts reconcile with the roster.

#### EAA-022 — Finish ETP / route-flying operations

Tasks:

- Promote `ETPBatch` into route/assignment records with airline, route, aircraft type, base, start/end, and operational status.
- Link each route assignment to pilots, instructors/check airmen, flights, checks, and evidence.
- Add route qualification milestones and completion gates.
- Keep airline/route fields out of the Part 141 data model.

Acceptance criteria:

- ETP dashboard shows route operations, not academic Part 141 cohort statistics.
- A pilot can have multiple route assignments with independent statuses and evidence.
- Route completion is based on flight/check records, not merely an order completion date.

### EPIC D — Scheduling and resource operations (P0)

#### EAA-030 — Build a conflict-aware scheduling engine

Tasks:

- Add schedule event, recurrence, location, duration, timezone, status, cancellation reason, and source fields.
- Model availability for students, instructors, aircraft, simulators, rooms, and maintenance windows.
- Implement overlap detection and configurable buffer/turnaround times.
- Add next-lesson suggestions based on prerequisites and student progress.
- Add calendar views: day, week, resource, instructor, student, cohort.

Acceptance criteria:

- The system prevents double-booking or requires a permissioned override with reason.
- A scheduler sees why a proposed booking is invalid.
- Rescheduling preserves the original event and records the reason.

#### EAA-031 — Add aircraft and simulator resource management

Tasks:

- Add aircraft registration/tail number, type, base, status, equipment, meter readings, and utilization counters.
- Add simulator type, availability, capacity, and credit rules.
- Add serviceability status and maintenance due thresholds.
- Track Hobbs/Tach start/end, block time, air time, simulator time, and discrepancies.

Acceptance criteria:

- Unserviceable or maintenance-due resources cannot be dispatched without authorized override.
- Utilization reports reconcile with completed flight records.
- Aircraft and simulator data are independent but schedulable resources.

#### EAA-032 — Add instructor/personnel management

Tasks:

- Add staff identity, role, qualifications, license/rating, currency, medical, ELP, duty limits, leave, and base.
- Add instructor-student allocation limits and workload views.
- Add instructor sign-off and electronic acknowledgement.
- Add instructor performance metrics based on completed lessons, stage checks, safety events, and student progress.

Acceptance criteria:

- Only qualified/current personnel can be assigned to restricted activities.
- Duty-limit and availability conflicts are visible before booking.
- Performance data is explainable and not used as an unreviewed punitive score.

### EPIC E — Dispatch, flight records, and progression (P0)

#### EAA-040 — Build the daily operations/dispatch board

Tasks:

- Add dispatch queue by base, date, aircraft, instructor, and student.
- Add electronic check-in, dispatch approval, aircraft release, return, and close-out.
- Add weather/ATC delay notes, cancellation reasons, no-show status, and overdue-aircraft alerts.
- Add operational status colors and a printable daily flight sheet.

Acceptance criteria:

- Operations staff can manage a full day without Excel.
- An overdue or open dispatch is visible immediately.
- Closed dispatches cannot be silently edited; corrections are audited.

#### EAA-041 — Add electronic lesson/flight records

Tasks:

- Record lesson type, route, aircraft/simulator, instructor, student, times, landings, approaches, fuel, defects, remarks, and sign-offs.
- Link record to syllabus lesson/objectives and training order where relevant.
- Support dual, solo, PIC, simulator, and ground-school activity types.
- Add instructor and student acknowledgement workflow.

Acceptance criteria:

- A completed flight updates hours, lesson progress, resource utilization, and student trajectory atomically.
- Total hours are broken down by activity type and aircraft/resource.
- A record cannot be finalized without required fields and signatures.

#### EAA-042 — Add assessments, stage checks, and gates

Tasks:

- Add assessment templates, grading criteria, examiner, attempt number, result, deficiencies, remedial plan, and next action.
- Add stage/checkride readiness rules.
- Add approval workflow for solo, stage completion, graduation, and ETP route completion.
- Add re-test and expiry rules.

Acceptance criteria:

- Readiness is calculated from objective evidence, not a manually typed status.
- Failed objectives remain visible until passed or formally waived.
- Waivers require an authorized user, reason, and audit event.

### EPIC F — Compliance and safety gates (P0)

#### EAA-050 — Implement medical and document expiry control

Tasks:

- Add medical class/type, issue date, expiry date, restrictions, examiner, document link, and verified status.
- Add configurable warning windows such as 90/30/7 days and expired.
- Add dashboard queues for missing, expiring, expired, and unverified records.
- Apply gates to scheduling, solo, dispatch, and checkride actions.

Acceptance criteria:

- An expired required medical creates a clear block or authorized override path.
- Expiry dates are based on configurable rules and visible source documents.
- Users can export an upcoming-expiry report.

#### EAA-051 — Implement ICAO English proficiency and license/currency controls

Tasks:

- Add ELP level, assessment date, validity/retest date, assessor, and evidence.
- Add license, rating, issue, expiry, revalidation, and limitation records.
- Add currency rules by activity and aircraft type.
- Add configurable ECAA/academy rule profiles.

Acceptance criteria:

- The system explains exactly which requirement blocks an operation.
- Rule profiles are versioned and do not rewrite prior decisions.
- Compliance status is available in student, instructor, dispatch, and dashboard views.

#### EAA-052 — Add safety and occurrence records

Tasks:

- Add hazard, occurrence, incident, defect, corrective action, owner, due date, and closure evidence.
- Restrict sensitive safety records by role.
- Add trend reporting without exposing unnecessary personal information.

Acceptance criteria:

- Safety items have accountable owners and due dates.
- Closure requires evidence or a documented acceptance.
- Safety data is auditable and exportable for internal review.

### EPIC G — Finance and cashier (P1)

#### EAA-060 — Add fee plans and student account ledger

Tasks:

- Add price lists by program, lesson/activity, aircraft type, simulator, instructor, and student contract.
- Add charges, credits, deposits, scholarships, refunds, waivers, and adjustments.
- Add student balance, aging, transaction source, and approval authority.
- Support EGP and future foreign-currency configuration without hard-coding currency assumptions.

Acceptance criteria:

- Every charge traces to a training activity or approved manual adjustment.
- Finance can reconcile deposits, charges, payments, refunds, and balance.
- Operational users cannot alter financial records without permission.

#### EAA-061 — Add invoices, receipts, and cashier workflow

Tasks:

- Add invoice/receipt numbering and printable bilingual templates.
- Add payment methods, cashier shift, receipt voiding, and end-of-day reconciliation.
- Export finance data to the academy’s accounting workflow.

Acceptance criteria:

- Voids and corrections retain the original financial event.
- End-of-day totals reconcile to ledger entries.
- Reports distinguish billed, collected, outstanding, refunded, and waived amounts.

### EPIC H — Security and governance (P0)

#### EAA-070 — Add users, roles, and page/action permissions

Tasks:

- Add local users with secure credential storage or integrate with an approved academy identity provider.
- Add roles such as admin, training director, registrar, ops desk, instructor, examiner, cashier, maintenance, and read-only auditor.
- Add page-level and action-level permissions: read, create, edit, approve, archive, export, override.
- Add location/base scoping.

Acceptance criteria:

- A user sees only permitted data and actions.
- Approval authority is separate from data entry where required.
- The app has a documented break-glass/emergency procedure.

#### EAA-071 — Add backup, restore, retention, and audit administration

Tasks:

- Add scheduled local backups, encrypted backup option, retention policy, and restore verification.
- Add backup health indicator and last verified restore date.
- Add audit viewer with filters by user, record, action, and date.
- Add data export package for institutional ownership.

Acceptance criteria:

- A restore drill can prove that student, document metadata, audit, and operational records recover.
- Backups are not silently overwritten beyond retention policy.
- Audit export is available to an authorized auditor.

### EPIC I — Reporting, analytics, and configurability (P0/P1)

#### EAA-080 — Build a report definition framework

Tasks:

- Add report definitions with filters, columns, grouping, sorting, parameters, permissions, and output format.
- Provide saved views for Part 141, ETP, Part 61, compliance, operations, finance, and demographics.
- Export PDF, XLSX, CSV, and printer-friendly Arabic/English layouts.
- Add scheduled report generation to a local folder or controlled email gateway when available.

Acceptance criteria:

- A manager can answer daily operational questions without editing code.
- Every KPI drills into the underlying records.
- Official reports include report version, generated time, scope, and source filters.

#### EAA-081 — Add operational dashboard and alerts

Tasks:

- Add today’s schedule, open dispatches, overdue aircraft, instructor utilization, student risks, compliance expiries, finance exceptions, and cohort progress.
- Add alert inbox with acknowledgement, owner, due date, and escalation.
- Add filters by base, program, cohort, date, and language.

Acceptance criteria:

- The dashboard supports a morning operations briefing.
- Alerts are actionable and link to the record requiring attention.
- Alert counts reconcile with filtered queues.

#### EAA-082 — Add configurable definitions and rules

Tasks:

- Move hard-coded statuses, nationalities, aircraft types, lesson types, document types, warning periods, and locations into admin definitions.
- Add effective dates and active/inactive flags.
- Add import/export of approved configuration packages.

Acceptance criteria:

- Authorized administrators can add a new aircraft type or document type without a code release.
- Historical records retain the definition used at the time.
- Configuration changes are audited and validated.

### EPIC J — Sync, integration, and field experience (P1/P2)

#### EAA-090 — Harden Excel import/export as a controlled integration

Tasks:

- Add import preview, row-level validation errors, duplicate resolution, and rollback.
- Add source file hash, import batch ID, operator, timestamp, and imported-row lineage.
- Separate authoritative database exports from editable templates.
- Add mapping profiles for legacy workbooks.

Acceptance criteria:

- A bad workbook cannot partially corrupt the database.
- Every imported field can be traced to a source row.
- Re-running the same file is idempotent or explicitly reports changes.

#### EAA-091 — Design local LAN synchronization

Tasks:

- Define a sync event envelope, device identity, sequence number, timestamp, and checksum.
- Classify entities as mergeable, centrally authoritative, or conflict-requiring review.
- Add a conflict inbox rather than silently choosing “last write wins.”
- Prototype sync between two disposable databases before integrating into production.

Acceptance criteria:

- Two offline terminals can exchange approved changes without duplicate students or orders.
- Conflicts are visible, explainable, and resolvable by an authorized user.
- Sync is resumable after interruption.

#### EAA-092 — Add field/mobile workflow

Tasks:

- Define the minimum field workflow: today’s roster, compliance status, start lesson, record activity, grade, sign, submit.
- Build it first as a responsive local web/LAN client or a focused Windows companion surface.
- Add offline queue and sync status.
- Do not expose full administration or finance to the field surface.

Acceptance criteria:

- An instructor can complete a lesson with minimal data entry.
- Offline submissions show pending/synced/conflict status.
- Field permissions are narrower than administrative permissions.

## 6. Delivery order

### Release R1 — Trustworthy core (P0)

Implement EAA-001 through EAA-004, EAA-010, EAA-011, EAA-020, EAA-021, EAA-022, EAA-050, EAA-070, and EAA-071.

Exit gate: reliable migrations, secure users/roles, complete Part 141/ETP separation, real document evidence, and auditable student/cohort records.

### Release R2 — Daily flight operations (P0)

Implement EAA-030 through EAA-042 and EAA-051.

Exit gate: the operations desk can schedule, dispatch, close, assess, and progress training without a parallel spreadsheet for the same workflow.

### Release R3 — Management control (P0/P1)

Implement EAA-052, EAA-080, EAA-081, EAA-082, and EAA-090.

Exit gate: management, compliance, and audit reports are reproducible, configurable, and traceable to source records.

### Release R4 — Finance and scale (P1/P2)

Implement EAA-060, EAA-061, EAA-091, and EAA-092.

Exit gate: finance is controlled, LAN operation is proven, and field users can work without turning the system into an uncontrolled shared database.

## 7. First execution sprint

Start with these ten tasks before building a visually larger dashboard:

1. EAA-001 — build/test baseline.
2. EAA-002 — migration framework.
3. EAA-004 — audit event foundation.
4. EAA-010 — student dossier model.
5. EAA-011 — document/attachment model.
6. EAA-020 — versioned curriculum model.
7. EAA-021 — Part 141 cohort completion.
8. EAA-022 — ETP route completion.
9. EAA-070 — user/role model.
10. EAA-071 — verified backup/restore drill.

Sprint definition of done:

- Schema migration exists and is tested.
- Each task has unit tests plus at least one UI smoke path where applicable.
- Arabic and English labels exist for new user-facing fields.
- Audit events are generated for writes.
- Existing Excel import, mirror, archive, and backup behavior still passes regression tests.
- A test database can be seeded with one Part 141 cohort, one ETP route, local and foreign students, documents, and at least one historical order.

## 8. What “better than Talon ETA” means for EAA

Do not compete by matching an arbitrary feature count. Win on operational fit:

- Talon-like breadth for curriculum, scheduling, records, dispatch, resources, billing, security, and reports.
- Better local resilience through offline-first operation and verified backup/restore.
- Better Arabic and bilingual handling throughout the data model, reports, and mixed Arabic/Latin aviation codes.
- Better EAA regulatory evidence through official order annexes, approval chains, ECAA rule profiles, and explainable gates.
- Better Part 141/ETP separation with cohort and route-specific workflows.
- Better institutional control through local data ownership, audit history, configurable definitions, and no forced cloud dependency.
- Better decision support through risk queues: students behind schedule, expiring medicals, unserviceable aircraft, overdue dispatches, instructor overload, unpaid balances, and missing evidence.

## 9. Risks to control

- **Scope explosion:** deliver R1/R2 before finance, mobile, or LAN sync.
- **Schema instability:** migrations and stable IDs must precede feature acceleration.
- **Unsafe automation:** never silently override medical, currency, maintenance, or approval gates.
- **Overloaded service layer:** split domain services before adding scheduling and dispatch.
- **Excel becoming authoritative again:** make imports previewable and database writes auditable.
- **False completion metrics:** calculate progress from lesson/objective records, not manually edited counters.
- **Identity/privacy risk:** apply roles and document access before adding more personal data.
- **Offline sync conflicts:** prototype with throwaway databases and explicit conflict review.

## 10. Definition of the target product

The target is not merely “a better orders app.” It is an offline-capable aviation training operations platform where:

- the registrar owns accurate identity and enrollment data;
- the training director owns programs, cohorts, approvals, and progression rules;
- the operations desk owns the day’s schedule, dispatch, and resource assignment;
- instructors record real training evidence;
- maintenance controls aircraft/simulator serviceability;
- compliance sees expiry and readiness risks;
- finance sees a traceable student ledger;
- management sees trustworthy KPIs and official reports;
- auditors can reconstruct what happened;
- students and field staff eventually get focused, permissioned views.

That is the practical path from the current EAA Training Manager to a Talon ETA-class system tailored for EAA.
