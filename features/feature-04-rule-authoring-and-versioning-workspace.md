# Feature 04 - Rule Authoring and Versioning Workspace

## 1. Feature overview
The Rule Authoring and Versioning Workspace allows engineering leads to create, test, approve, and publish validation rules and profile-specific rule packs without code changes.

Why it matters:
- Compliance logic evolves with standards, manufacturing capability, and product line constraints.
- Rule governance must be auditable and controlled for enterprise adoption.

User problem solved:
- Today rule updates require engineering code deployments, creating bottlenecks and inconsistency.

## 2. System role
Where it sits:
- Platform control plane for the full validation ecosystem.
- Source of truth for rule definitions, profiles, exceptions, and lifecycle status.

Depends on:
- Identity and RBAC service for author, reviewer, and approver roles.
- Rule compiler and simulation harness.
- Persistent store for versioned rule artifacts.

Features depending on it:
- Feature 01 and 02 consume active rule packs for execution and visualization.
- Feature 03 depends on machine-readable rule rationale for recommendations.
- Feature 05 embeds rule pack versions in reports.
- Feature 06 uses rule profiles as model context features.
- Feature 07 links collaboration decisions to active policy version.

## 3. Architecture design
Assumptions:
- Rule expressions can be represented in a constrained DSL or JSON rule schema.
- Some rules require deterministic evaluators, not only threshold checks.

Core components:
- Rule Authoring UI: create rules, thresholds, exceptions, and metadata.
- Rule DSL Parser and Validator: schema and semantic validation.
- Rule Compiler: converts authored rules into executable artifacts.
- Simulation Sandbox: run candidate rules against historical design samples.
- Approval Workflow Service: draft, review, approved, published lifecycle.
- Rule Registry API: fetch active pack by project, product family, and date.
- Version Store: immutable artifacts and changelog.

APIs and event flows:
- `POST /rule-packs/draft`
- `POST /rule-packs/{id}/simulate`
- `POST /rule-packs/{id}/approve`
- `POST /rule-packs/{id}/publish`
- Event `rule-pack.published` triggers worker cache refresh.

Data flow input to output:
1. Lead authors or edits rules in workspace.
2. DSL validator checks syntax and semantic constraints.
3. Simulation executes against selected baseline designs.
4. Reviewer approves and publishes pack.
5. Registry marks new pack as active by scope.
6. Validation workers hot-reload updated artifacts.

## 4. Detailed build plan
Work split:
- Frontend team: authoring workflows, diff view, simulation result UI.
- Backend team: registry API, workflow engine, artifact management.
- Rule engine team: compiler adapters and compatibility checks.
- Data team: historical sample selection for simulation.
- Security team: approval controls and immutable audit log.

Integration contracts:
- `RuleDefinitionSchema` with strict type and unit metadata.
- `CompiledRuleArtifact` contract for worker runtime.
- `RulePackActivation` contract consumed by validation orchestrator.

Implementation order:
1. Define DSL and metadata taxonomy.
2. Build registry and immutable version storage.
3. Add draft and approval workflow.
4. Integrate simulation harness.
5. Add publish event and runtime cache invalidation.
6. Add policy diff and rollback workflow.

## 5. Data model and schema considerations
Core entities:
- `RuleDefinition(rule_id, name, category, severity_default, expression_json, rationale_text)`
- `RulePackVersion(pack_version_id, pack_name, semantic_version, status, created_by, created_at)`
- `RulePackEntry(entry_id, pack_version_id, rule_id, threshold_override_json, enabled)`
- `RuleException(exception_id, pack_version_id, scope_type, scope_id, expires_at, justification)`
- `RuleApproval(approval_id, pack_version_id, reviewer_id, decision, notes, decided_at)`

Relationships:
- One pack version has many entries and exceptions.
- One rule can appear in many pack versions.

Versioning strategy:
- Immutable semantic versions with explicit activation windows.
- Rollback by activation pointer switch, not by mutating historical records.

Auditability and traceability:
- Full changelog with before and after diffs.
- Approval signatures and publish actor stored immutably.

## 6. Validation and observability
Testing strategy:
- Parser and compiler contract tests.
- Simulation correctness tests using golden datasets.
- Migration tests for backward compatibility of old packs.

Logging and monitoring:
- Metrics: publish frequency, simulation failure rate, runtime cache hit ratio.
- Alerts for pack activation mismatch between registry and workers.

Failure handling and recovery:
- Failed publish does not alter active pointer.
- Worker runtime keeps previous valid pack on compile or load failure.

## 7. Risks and edge cases
Technical risks:
- DSL complexity can become hard to validate and maintain.
- Inconsistent units across rules can create false failures.

Product risks:
- Overly permissive exception workflows can weaken standards.

Performance bottlenecks:
- Large simulation runs can delay approval cycle.

Data quality issues:
- Historical baseline data may not represent new product lines.

Human workflow risks:
- Approval bottlenecks if too few reviewers are authorized.

## 8. MVP vs production roadmap
MVP first:
- Rule CRUD, simple approval, semantic versioning, and activation.
- Limited simulation over curated sample set.

Deferred:
- Advanced what-if scenario analysis.
- Conditional rules with complex compositional logic.

Production maturity:
- Policy-as-code governance with peer review and automated quality gates.
- Multi-tenant policy branching by business unit.

## 9. Final recommendation
Recommended approach:
- Build a constrained rule DSL with strong typing and immutable versioned packs.

Tradeoffs considered:
- Free-form scripting offers flexibility but high risk and weak governance.
- Constrained DSL reduces flexibility but provides safety, auditability, and stable worker execution.

Why this fit is best:
- It balances non-developer rule ownership with production-grade reliability and traceability.

## 10. Agent implementation case scenarios
Scenario A - Draft to publish happy path:
- Trigger: standards lead creates draft with threshold updates.
- Build exactly: enforce schema validation, run simulation against baseline pack, require two-role approval, then publish immutable artifact.
- Contract details: `RulePackActivation` includes `pack_version_id`, `activation_scope`, `effective_from`, `published_by`, `approval_ids`.
- Done criteria: published pack is retrievable and activated across worker fleet within rollout target.

Scenario B - Emergency rollback after regression:
- Trigger: post-publish monitoring detects false-positive spike.
- Build exactly: switch activation pointer to previous approved pack without mutating any historical records.
- Contract details: rollback emits `rule-pack.rolled-back` with `from_pack_version_id`, `to_pack_version_id`, and `incident_id`.
- Done criteria: new runs use rollback pack immediately and audit trail links to incident.

Scenario C - Exception expiration handling:
- Trigger: `RuleException.expires_at` passes during active sessions.
- Build exactly: scheduler marks exception inactive and publishes cache invalidation event.
- Contract details: worker runtime receives effective exceptions list with version hash.
- Done criteria: expired exceptions never remain active past grace window.

## 11. Agent orchestration and tool calls
This feature is the governance control plane for the overall architecture in [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md).

Required tool calls:
- `list_rule_packs`
- `validate_rule_definition`
- `simulate_rule_pack`
- `approve_rule_pack`
- `publish_rule_pack`
- `rollback_rule_pack`

Agent orchestration notes:
- The agent may help draft and explain rule changes, but publish and rollback actions are always `admin` tools with approval requirements.
- Simulation should be available as a safe compute tool so the agent can compare pack effects before any governance action is proposed.
- Published rule-pack metadata must flow back into Features 01, 03, 05, 06, and 07 as canonical provenance.

