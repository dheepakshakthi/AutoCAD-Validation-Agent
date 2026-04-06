# Feature 07 - Team Collaboration Workflow

## 1. Feature overview
The Team Collaboration Workflow adds assignable issues, threaded discussions, decision states, and verification checkpoints around violations so compliance remediation becomes a managed team process.

Why it matters:
- Validation is multi-role work involving design, manufacturing, quality, and lead reviewers.
- Shared ownership and traceable decisions reduce hidden risk and rework.

User problem solved:
- Current workflows fragment across chat, email, and spreadsheets with weak traceability.

## 2. System role
Where it sits:
- Coordination layer on top of validation, recommendations, and rule governance.
- Connects technical findings to accountable execution.

Depends on:
- Feature 01 and 02 for issue origin and geometry evidence.
- Feature 03 for proposed fix options.
- Identity, RBAC, and notification services.

Features depending on it:
- Feature 05 report generation consumes issue lifecycle and approvals.
- Program governance depends on issue closure and accepted risk records.

## 3. Architecture design
Assumptions:
- Enterprise users require role-based permissions and audit logs for accepted risks.

Core components:
- Issue Service: create, assign, transition, and close issues.
- Comment and Mention Service: threaded discussions with context links.
- Workflow State Machine: `open`, `accepted-risk`, `fixed`, `verified`, `closed`.
- Notification Service: in-app, email, and webhook alerts.
- Activity Timeline Service: immutable event log for every issue action.
- CAD Deep Link Adapter: open issue and focus exact annotation in model.

APIs and events:
- `POST /issues` from panel or annotation action.
- `PATCH /issues/{id}` for status, assignee, and accepted risk notes.
- `POST /issues/{id}/comments`.
- Events: `issue.created`, `issue.updated`, `issue.verified`, `issue.accepted-risk`.

Data flow input to output:
1. Engineer creates issue from violation or annotation.
2. Assignee receives notification and reviews recommendation options.
3. Assignee marks fixed after geometry update.
4. Verification step checks latest run status before closure.
5. Timeline and report artifacts include full decision trail.

## 4. Detailed build plan
Work split:
- Backend team: issue APIs, workflow state machine, event publication.
- Frontend team: issue board, detail drawer, mention and comment UX.
- CAD integration team: deep-link launch and selection sync.
- Product team: lifecycle policy and ownership rules.
- DevOps and platform team: notification routing and delivery reliability.

Integration contracts:
- `IssueSourceRef` links issue to violation ID, run ID, and entity refs.
- `IssueStateTransition` contract with role and policy checks.
- `IssueVerificationResult` contract from validation service.

Implementation order:
1. Deliver core issue entity and assignment.
2. Add workflow transitions and policy checks.
3. Add threaded comments and mentions.
4. Integrate verification gate with latest validation run.
5. Add notifications and SLA dashboards.

## 5. Data model and schema considerations
Core entities:
- `Issue(issue_id, project_id, source_type, source_ref_json, status, severity, assignee_id, created_at)`
- `IssueComment(comment_id, issue_id, author_id, body, created_at)`
- `IssueTransition(transition_id, issue_id, from_status, to_status, actor_id, reason, created_at)`
- `AcceptedRiskRecord(record_id, issue_id, approver_id, justification, expires_at)`
- `IssueVerification(verification_id, issue_id, run_id, result, verified_by, verified_at)`

Relationships:
- One issue has many comments and transitions.
- Accepted risk records are optional but require approval linkage.

Versioning strategy:
- Immutable transition log.
- Current issue state is derived from last valid transition.

Auditability and traceability:
- Every state change includes actor, timestamp, and reason.
- Accepted risk requires mandatory structured justification.

## 6. Validation and observability
Testing strategy:
- State machine transition tests with role-based policy matrix.
- End-to-end tests from violation creation to verified closure.
- Notification delivery and retry tests.

Logging and monitoring:
- Metrics: time-to-first-response, mean time to verification, reopened issue rate.
- Alerts on stuck issues breaching SLA thresholds.

Failure handling and recovery:
- Idempotent transition commands to prevent duplicate updates.
- Outbox pattern for reliable event and notification delivery.

## 7. Risks and edge cases
Technical risks:
- Race conditions when multiple users update same issue.
- Cross-version mismatch between issue source run and current geometry.

Product risks:
- Overly complex workflow can reduce adoption.

Performance bottlenecks:
- Activity timeline queries at portfolio scale.

Data quality issues:
- Incomplete issue context if source refs are missing.

Human workflow risks:
- Teams may bypass formal issue states through informal communication.

## 8. MVP vs production roadmap
MVP first:
- Issue creation, assignment, basic states, and comments.
- Manual verification marker.

Deferred:
- SLA automation and escalation policies.
- Advanced analytics and workload balancing.

Production maturity:
- Full policy-driven workflow with automated verification checks.
- Enterprise notifications and external ticketing integrations.

## 9. Final recommendation
Recommended approach:
- Implement collaboration as a strict state machine with immutable activity history.

Tradeoffs considered:
- Flexible free-form updates are easy to start but weak for governance.
- Explicit workflow adds process overhead but ensures accountability and traceability.

Why this fit is best:
- It aligns engineering execution with compliance governance and closes the loop from detection to verified remediation.

## 10. Agent implementation case scenarios
Scenario A - Issue created from annotation click:
- Trigger: user clicks annotation and chooses create issue.
- Build exactly: create issue with immutable `IssueSourceRef` linking `violation_id`, `run_id`, `entity_ref`, and `geometry_version_id`.
- Contract details: `POST /issues` must support idempotency key to prevent duplicate issues on repeated clicks.
- Done criteria: duplicate click within retry window creates exactly one issue.

Scenario B - Concurrent updates by multiple users:
- Trigger: assignee and reviewer modify issue status at same time.
- Build exactly: enforce optimistic locking with `version` field and reject stale update with conflict payload.
- Contract details: `IssueStateTransition` includes `from_version`, `to_status`, `actor_role`, `reason_code`.
- Done criteria: no lost updates and full transition order preserved.

Scenario C - Verification fails after claimed fix:
- Trigger: user moves issue to `fixed`, but latest run still contains violation.
- Build exactly: block transition to `verified`, attach verification failure reason, and reopen issue automatically if closed prematurely.
- Contract details: `IssueVerificationResult` includes `verification_run_id`, `still_failing_rule_ids`, `checked_at`.
- Done criteria: issues cannot close while linked violations remain active.

## 11. Agent orchestration and tool calls
This feature is the execution and accountability layer that closes the loop started by the agent architecture in [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md).

Required tool calls:
- `create_issue`
- `assign_issue`
- `add_issue_comment`
- `transition_issue_state`
- `verify_issue_fix`
- `notify_issue_watchers`

Future supporting tool calls:
- `get_violation_context`
- `highlight_violations`

Agent orchestration notes:
- The agent should be able to create draft issues automatically from severe findings, but assignment and state transitions must respect workflow policy.
- Verification must always call back into the latest validation state rather than rely on conversational assertions.
- Collaboration artifacts should remain linked to the same `run_id`, `violation_id`, and `geometry_version_id` used elsewhere in the platform.

