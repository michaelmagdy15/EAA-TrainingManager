# EAA-TMS Default Permission Matrix

Permission levels are ordered: `None < ReadOnly < LimitedEdit < FullEdit < Approve`. Grants are checked in `DatabaseService` for direct service reads/writes and in the navigation shell before pages/actions are exposed. A grant at a higher level implies lower levels.

| Role | Students | Training orders | Schedule | Flight records | Assessments | Resources | Compliance | Finance | Audit | Users | Locations | Personnel | Dashboard | System admin | Curriculum | Regulatory documents | Official records |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Administrator | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve | Approve |
| Training manager | Approve | Approve | FullEdit | FullEdit | Approve | FullEdit | FullEdit | None | ReadOnly | ReadOnly | ReadOnly | FullEdit | ReadOnly | None | Approve | None | None |
| Dispatch | ReadOnly | ReadOnly | Approve | None | None | FullEdit | ReadOnly | None | ReadOnly | None | None | None | None | None | None | None | None |
| Instructor | ReadOnly | None | ReadOnly | FullEdit | FullEdit | ReadOnly | ReadOnly | None | None | None | None | ReadOnly | None | None | ReadOnly | None | None |
| Examiner | ReadOnly | None | None | ReadOnly | Approve | None | None | None | ReadOnly | None | None | None | None | None | ReadOnly | None | None |
| Finance | ReadOnly | ReadOnly | None | None | None | None | None | FullEdit | ReadOnly | None | None | None | None | None | None | None | None |
| Maintenance | None | None | ReadOnly | ReadOnly | None | FullEdit | None | None | None | None | None | None | None | None | None | None | None |
| Student | None | None | None | None | None | None | None | None | None | None | None | None | None | None | None | None | Approve |
| Auditor | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | ReadOnly | None | ReadOnly | ReadOnly | None |
| Regulatory compliance | None | None | None | None | None | None | Approve | None | ReadOnly | None | None | None | None | None | Approve | Approve | None |

The Student role links a user account to a student profile through `Users.StudentId` (schema version 13) and grants `Approve` on `official-records` so the trainee can acknowledge receipt of their own official training record in their own authenticated session. User accounts receive one or more assigned locations. Login sessions must select one of those assigned active locations; each session and audit event records that location.

## Approval-controlled transitions

- Completing or reopening a training order requires the caller to have `Approve` on `training-orders`.
- The operator re-enters their password, supplies a reason, and creates a 15-minute approval record bound to user, session, location, entity, and transition.
- The approval is consumed once in the same transaction as the order transition. A repeated or expired approval is rejected.
- Approving or superseding a controlled regulatory document requires `Approve` on `regulatory-documents`; accepting a program approval requires `Approve` on `regulatory-documents`; publishing a curriculum version or waiving a training objective requires `Approve` on `curriculum`. Each consumes a matching unused approval record in the same transaction, and mismatched evidence is rejected.
- Approval records, approval-use rows, audit events, and objective-progress rows are append-only at the SQLite layer.

## Operational boundaries

- Disabling an account revokes its active sessions while preserving the user and attribution history.
- Audit history can be filtered by date, actor, action, entity type, and entity ID, then exported as CSV by an audit-authorized account.
- The initial location catalog contains Misr Flying College, October Airport, El-Tor, and Asyut. User/location assignments currently gate sign-in; associating every legacy training/resource record with a location and enforcing row-level branch filtering remains a follow-up before multi-site rollout.
- Role grants are seeded by the versioned application policy and persisted in SQLite. The operator UI supports account creation and disabling; editing arbitrary role-grant policy is reserved for designated security administration work.
