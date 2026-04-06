# Feature 01 - Live Compliance Guardrail Panel

## 1. Feature overview
The Live Compliance Guardrail Panel continuously evaluates design changes while an engineer edits geometry in AutoCAD and surfaces pass or fail state by rule category and severity.

Why it matters:
- Prevents late discovery of standards violations.
- Reduces manual checklist time during modeling.
- Turns compliance into a continuous feedback loop instead of a review milestone.

User problem solved:
- Engineers currently rely on memory and periodic manual checks.
- Violations are often found during handoff or formal design review, causing rework.

## 2. System role
Where it sits:
- Primary real-time feedback surface in the CAD plugin UI.
- Consumer of the rule execution pipeline and producer of state for downstream features.

Depends on:
- CAD change stream from plugin geometry listeners.
- Rule execution service and active rule pack.
- Session state and violation store.
- Identity and project profile context.

Features depending on it:
- Feature 02 In-Canvas Issue Highlighting (needs violation references).
- Feature 03 AI Fix Recommendation Assistant (needs violation context).
- Feature 05 Automated Validation Report Generator (needs category and severity snapshots).
- Feature 06 Risk Prediction Before Finalization (blends predictive and deterministic risk in one panel).
- Feature 07 Team Collaboration Workflow (opens issues from panel findings).

## 3. Architecture design
Assumptions:
- AutoCAD plugin can emit geometry delta events with stable model version IDs.
- Rule execution can complete a normal delta check in under 2 seconds for medium assemblies.

Core components:
- CAD Plugin Event Adapter: emits `geometry.delta` with model, entity refs, and operation metadata.
- Delta Normalizer Service: converts CAD-native operations into canonical geometry change payloads.
- Validation Orchestrator: debounces changes, schedules checks, and handles cancellation of stale runs.
- Rule Engine Workers: execute category-specific checks in parallel pools.
- Guardrail Query API: returns latest status by category and severity.
- Guardrail Panel UI Module: renders pass or fail cards, severity counters, and trend sparkline.
- Redis Session Cache: stores most recent run state for low-latency UI refresh.
- PostgreSQL Validation Store: source of truth for historical runs and audit trail.

Event and API flow:
- Event `geometry.delta` -> queue topic `validation.requested`.
- Workers emit `validation.completed` with violations and category summary.
- Guardrail API endpoint `GET /projects/{projectId}/sessions/{sessionId}/guardrail-status`.
- Optional server push via WebSocket channel `guardrail.{sessionId}` for near real-time UI updates.

Data flow input to output:
1. User edits geometry in CAD.
2. Plugin sends normalized delta and model version.
3. Orchestrator coalesces rapid edits and triggers rule workers.
4. Rule workers return category-level pass or fail and violation details.
5. Guardrail service updates cache and persistent store.
6. Panel receives push or polls and updates severity-ranked categories.

## 4. Detailed build plan
Work split by engineering function:
- Frontend CAD team: panel UI, optimistic update states, stale-run indicators.
- Backend platform team: orchestrator, run lifecycle, API, cache invalidation.
- Rule engine team: worker contract, rule category execution interfaces.
- DevOps team: queue topology, autoscaling policies, SLO dashboards.
- Product engineering: category taxonomy and severity policy configuration.

Integration contracts:
- `ValidationRequest` contract owned by platform team.
- `RuleEvaluationResult` contract co-owned by platform and rule engine teams.
- `GuardrailStatusDTO` contract owned by frontend and API teams.

Implementation order and handoffs:
1. Define canonical event schema and run state machine.
2. Build backend run ingestion and storage.
3. Expose read API with seeded data.
4. Implement panel UI against mock API.
5. Integrate real worker outputs.
6. Add push updates and debouncing.
7. Harden with performance tests and production telemetry.

## 5. Data model and schema considerations
Core entities:
- `DesignSession(session_id, user_id, project_id, active_rule_pack_version, started_at)`
- `GeometryVersion(version_id, session_id, parent_version_id, change_count, created_at)`
- `ValidationRun(run_id, session_id, geometry_version_id, status, started_at, ended_at)`
- `Violation(violation_id, run_id, rule_id, category, severity, entity_ref, message, status)`
- `GuardrailCategorySnapshot(snapshot_id, run_id, category, pass_fail, severity_counts_json)`

Relationships:
- One session has many geometry versions.
- One geometry version can have one or more validation runs if retries occur.
- One run has many violations and category snapshots.

Versioning strategy:
- Immutable geometry and run records.
- Soft-close stale runs when a newer geometry version supersedes them.

Auditability and traceability:
- Persist rule pack version and worker build hash per run.
- Keep deterministic replay payload for incident analysis.

## 6. Validation and observability
Testing strategy:
- Contract tests for event schema and API DTO.
- Load tests simulating high-frequency edit bursts.
- End-to-end tests from CAD edit to panel update.

Logging and monitoring:
- Correlation IDs across plugin event, run, worker, and UI push.
- Metrics: check latency p50 p95, stale run cancel rate, violation volume by category.
- Alerts: queue backlog growth, latency SLO breach, worker failure spikes.

Failure handling and recovery:
- On worker timeout, show category state as `degraded` not `pass`.
- Retry transient worker failures with bounded exponential backoff.
- Reconcile cache from persistent store on service restart.

## 7. Risks and edge cases
Technical risks:
- Event storm from rapid edits can overload worker pool.
- Topology changes can invalidate previous entity references.

Product risks:
- Too many medium or low severity alerts can cause alert fatigue.

Performance bottlenecks:
- Serialization overhead for large assemblies.

Data quality issues:
- Inconsistent category mapping if rule metadata is not normalized.

Human workflow risks:
- Engineers may distrust panel if stale results are not clearly labeled.

## 8. MVP vs production roadmap
MVP first:
- Poll-based panel refresh every 2 to 3 seconds.
- Core categories and static severity thresholds.
- Single active rule pack per project.

Deferred to later:
- WebSocket push updates.
- Dynamic user-level severity tuning.
- Multi-project benchmark analytics.

Mature production target:
- Adaptive debounce and workload-aware run scheduling.
- Reliability SLO at 99.9 percent for status update path.

## 9. Final recommendation
Recommended approach:
- Build a run-oriented event pipeline with explicit versioned state, then layer low-latency cache and UI push.

Tradeoffs considered:
- Pure synchronous inline checks were rejected because they block CAD editing.
- Fully async queue-based validation introduces eventual consistency but scales better and isolates failures.

Why this fit is best:
- It preserves editing flow, supports enterprise-scale assemblies, and creates the canonical compliance state required by all downstream features.

## 10. Agent implementation case scenarios
Scenario A - Rapid edit burst on a single assembly:
- Trigger: 15 or more geometry deltas arrive within 2 seconds for one `session_id`.
- Build exactly: debounce window 300 milliseconds, create one new `ValidationRun` for the newest `geometry_version_id`, and mark older in-flight runs as `superseded`.
- Contract details: `validation.requested` must include `run_id`, `session_id`, `geometry_version_id`, `rule_pack_version`, `correlation_id`, and `requested_at`.
- Done criteria: panel never displays a stale run as current, and superseded runs never emit final `pass` cards.

Scenario B - Worker timeout or partial category failure:
- Trigger: one category worker exceeds timeout while others succeed.
- Build exactly: complete run with status `degraded`, include `category_state` per category (`pass`, `fail`, `unknown`), and keep unresolved categories out of aggregate pass counts.
- Contract details: `GuardrailStatusDTO` includes `run_status`, `category_state`, `severity_counts`, `degraded_reason_codes`.
- Done criteria: UI shows clear degraded banner and no false-green summary.

Scenario C - Rule pack changes during active editing:
- Trigger: `rule-pack.published` arrives while user session is active.
- Build exactly: pin current in-flight runs to old `rule_pack_version`; only runs enqueued after publish use new version.
- Contract details: every `ValidationRun` persists immutable `rule_pack_version` and `worker_build_hash`.
- Done criteria: replaying run history reproduces original results exactly.

## 11. Agent orchestration and tool calls
This feature is the deterministic backbone for the unified agent architecture described in [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md).

Required tool calls:
- `get_active_document_context`
- `extract_geometry_delta`
- `extract_geometry_snapshot`
- `run_validation`
- `get_guardrail_status`
- `get_validation_run`
- `subscribe_guardrail_updates`

Agent orchestration notes:
- Validation is triggered by geometry changes or explicit validate requests, not by free-form chat alone.
- The agent may summarize findings, prioritize categories, and recommend next actions, but it must not override deterministic pass or fail outcomes.
- Feature 01 becomes a workspace module inside the unified CADY surface rather than a permanently separate palette.
