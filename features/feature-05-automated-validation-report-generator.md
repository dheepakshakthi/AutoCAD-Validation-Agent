# Feature 05 - Automated Validation Report Generator

## 1. Feature overview
The Automated Validation Report Generator creates downloadable validation artifacts after each check cycle, including issue summary, severity distribution, affected components, annotation evidence, and compliance score.

Why it matters:
- Design reviews, sign-off gates, and customer audits require defensible documentation.
- Automated reports remove manual screenshot and spreadsheet work.

User problem solved:
- Teams spend significant time producing and reconciling compliance evidence.

## 2. System role
Where it sits:
- Evidence and governance layer that consumes outputs from validation, annotation, recommendation, and collaboration services.

Depends on:
- Feature 01 and 02 for violation and spatial evidence.
- Feature 04 for rule pack provenance.
- Feature 07 for issue state and resolution status.

Features depending on it:
- Program governance and release workflows.
- External quality audits and sign-off checkpoints.

## 3. Architecture design
Assumptions:
- Reports are needed in PDF for review and JSON for machine ingestion.

Core components:
- Report Orchestrator: listens for run completion and initiates assembly.
- Evidence Aggregator: collects violations, annotations, recommendations, and issue states.
- Report Renderer Worker: renders templates to PDF and JSON.
- Asset Service: stores screenshots, report files, and metadata.
- Compliance Scoring Service: computes weighted score by category and severity.
- Report API: retrieval, filtering, and download access.

APIs and events:
- Event `validation.completed` and `report.requested`.
- `POST /reports/generate` with run IDs and template ID.
- `GET /reports/{reportId}` metadata.
- `GET /reports/{reportId}/download?format=pdf|json`.

Data flow input to output:
1. Validation run completes.
2. Orchestrator requests annotation snapshots and issue state.
3. Scoring service computes compliance score.
4. Renderer generates PDF and JSON outputs.
5. Asset service persists files with immutable checksums.
6. Users download reports from UI or API.

## 4. Detailed build plan
Work split:
- Backend platform team: orchestration, rendering queue, report APIs.
- Frontend team: report center UI, filters, and compare view.
- CAD integration team: high-resolution annotated screenshot capture.
- Data team: compliance scoring policy implementation.
- DevOps: storage lifecycle policies and secure download path.

Integration contracts:
- `ReportInputBundle` contract with run, rule pack, and issue references.
- `ComplianceScoreBreakdown` contract used in UI and API.
- `ReportArtifact` contract with checksum and retention metadata.

Implementation order:
1. Build JSON report first as canonical representation.
2. Add PDF rendering templates for review-friendly output.
3. Integrate screenshot capture and issue timeline.
4. Add scoring rationale details and comparison mode.
5. Add retention and archive policies.

## 5. Data model and schema considerations
Core entities:
- `ValidationReport(report_id, run_id, template_id, format, status, created_at)`
- `ReportSection(section_id, report_id, section_type, payload_json)`
- `ComplianceScore(score_id, report_id, overall_score, category_breakdown_json)`
- `ReportArtifact(artifact_id, report_id, object_key, checksum, size_bytes, retention_until)`

Relationships:
- One run can have multiple reports by template or format.
- One report can reference many artifacts and snapshots.

Versioning strategy:
- Report template version stored per report.
- Regeneration creates a new report ID, never overwrites previous artifacts.

Auditability and traceability:
- Include source run ID, geometry version, and rule pack version in every report.
- Persist checksums and generation actor.

## 6. Validation and observability
Testing strategy:
- Snapshot tests for JSON schema and PDF section rendering.
- End-to-end tests from run completion to downloadable report.
- Access control tests for report visibility.

Logging and monitoring:
- Metrics: report generation latency, render failure rate, artifact storage errors.
- Alert on repeated rendering failures by template version.

Failure handling and recovery:
- Retry render worker on transient template or storage failures.
- If screenshot capture fails, generate report with explicit missing-evidence marker.

## 7. Risks and edge cases
Technical risks:
- Heavy PDF rendering can saturate worker CPUs.
- Template drift can break backward compatibility.

Product risks:
- Compliance score without transparent rationale can be challenged by reviewers.

Performance bottlenecks:
- Very large assemblies with many screenshots inflate generation time and file size.

Data quality issues:
- Missing or stale issue status can produce misleading reports.

Human workflow risks:
- Teams may treat score as final truth without reviewing critical severity details.

## 8. MVP vs production roadmap
MVP first:
- JSON plus basic PDF report with key sections.
- Per-run on-demand generation.

Deferred:
- Scheduled portfolio reporting.
- Multi-run trend analytics and benchmark dashboards.

Production maturity:
- Template governance with approval workflow.
- Legally defensible archival and retention controls.

## 9. Final recommendation
Recommended approach:
- Use canonical JSON report generation first, then render human-friendly formats from the same canonical payload.

Tradeoffs considered:
- Rendering directly to PDF is fast to ship but weak for downstream integrations.
- Canonical JSON introduces an extra layer but improves durability and interoperability.

Why this fit is best:
- It serves both enterprise governance workflows and future data products without rework.

## 10. Agent implementation case scenarios
Scenario A - Standard report generation on run completion:
- Trigger: `validation.completed` for run with full evidence set.
- Build exactly: create immutable `ReportInputBundle` snapshot, render canonical JSON first, then generate PDF from frozen JSON payload.
- Contract details: `ReportInputBundle` includes `run_id`, `geometry_version_id`, `rule_pack_version`, `violation_ids`, `annotation_snapshot_ids`, `issue_snapshot_id`, `frozen_at`.
- Done criteria: JSON and PDF artifacts share same provenance hash and checksum chain.

Scenario B - Missing evidence assets:
- Trigger: screenshot or issue timeline unavailable at render time.
- Build exactly: continue generation with explicit placeholder block and `missing_evidence` section.
- Contract details: `ReportArtifact` includes `completeness_status` and `missing_sections`.
- Done criteria: report never silently omits unavailable evidence.

Scenario C - Regeneration after template update:
- Trigger: user regenerates historical run with new template version.
- Build exactly: produce new `report_id` with preserved source snapshot reference; do not overwrite previous artifacts.
- Contract details: every report stores `template_version` and `source_snapshot_hash`.
- Done criteria: prior reports remain downloadable and unchanged.

## 11. Agent orchestration and tool calls
This feature should be driven by explicit report tools from [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md), not by ad hoc screen scraping or manual export steps.

Required tool calls:
- `get_report_input_bundle`
- `capture_annotated_view`
- `compute_compliance_score`
- `generate_validation_report`
- `get_report_status`

Future supporting tool calls:
- `get_validation_run`
- `get_conversion_status`

Agent orchestration notes:
- The agent may recommend report generation automatically after important runs, but report creation should still freeze a canonical input bundle before rendering.
- Reports must include the same run, rule-pack, recommendation, and issue provenance the agent used during reasoning.
- Report generation should be asynchronous so it does not block the interactive workspace.

