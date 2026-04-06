# Feature 00 - Unified Agentic Compliance Architecture

## 1. Purpose
This document defines the target architecture for turning the current split experience into one agentic AutoCAD compliance workspace.

Current state:
- `ShowAiAssistant` is a chat-first surface.
- `ShowGuardrailPanel` is a validation-first surface.
- Validation and recommendations are separated by UI boundary instead of connected by typed tools.

Target state:
- One unified `CADY Workspace` palette hosts chat, guardrail status, issue details, recommendation cards, reports, and action approvals.
- Deterministic validation runs automatically from geometry changes.
- The agent reasons over tool outputs and invokes approved tool calls instead of emitting ad hoc JSON blocks.

## 2. Architectural principles
- Deterministic validation is never optional and never delegated to the LLM.
- The LLM can plan, summarize, rank, and decide which tools to call, but business truth comes from typed services and AutoCAD APIs.
- Every capability exposed to the agent must be a named tool with schema-validated inputs and outputs.
- Read and compute tools may run automatically; write and admin tools require explicit approval unless pre-approved by policy.
- Every tool result must be traceable to `session_id`, `geometry_version_id`, `run_id`, and `audit_ref`.
- The AutoCAD plugin remains the only trusted executor for in-CAD actions.

## 3. Runtime topology
### 3.1 AutoCAD plugin layer
Responsibilities:
- Capture geometry deltas and full geometry snapshots.
- Host local AutoCAD tools such as entity focus, highlighting, selection sync, and safe edits.
- Maintain document-scoped session context.
- Render the unified WPF workspace.

### 3.2 Python backend layer
Responsibilities:
- Run the primary agent orchestration loop.
- Expose deterministic validation, rule-pack, report, issue, risk, and conversion services.
- Maintain run history, issue state, and report artifacts.
- Handle provider-specific model clients behind one agent abstraction.

### 3.3 Data and orchestration layer
Responsibilities:
- Store validation runs, violations, rule packs, reports, issues, and job states.
- Publish run and issue lifecycle events.
- Maintain append-only audit records for agent actions and approvals.

## 4. Agent operating modes
### 4.1 Passive monitor
- Triggered by geometry changes or validation completion.
- Summarizes the latest run.
- Suggests next actions without mutating the drawing.

### 4.2 Interactive copilot
- Triggered by user questions, issue selection, or follow-up requests.
- Uses read and compute tools to gather context and answer with traceable evidence.

### 4.3 Approval executor
- Triggered when the user approves a proposed edit, publish, rollback, or report action.
- Executes only approved write and admin tools.
- Records an immutable audit entry for each applied action.

## 5. Tool contract standard
Every tool call should use the same baseline envelope.

Request envelope:
- `session_id`
- `project_id`
- `drawing_id`
- `geometry_version_id`
- `run_id` when applicable
- `dry_run`
- `approval_token` for write and admin actions

Response envelope:
- `ok`
- `data`
- `warnings`
- `errors`
- `audit_ref`
- `requires_approval`
- `next_recommended_tools`

Tool classes:
- `read`: fetches context only
- `compute`: performs analysis without mutating state
- `write`: changes UI state, issue state, or CAD state
- `admin`: governance actions such as rule publish or rollback

Rejected tool patterns:
- Arbitrary shell execution
- Free-form `run_any_autocad_command`
- Direct SQL access from the agent
- Unbounded file system access from the LLM

## 6. Required tool registry
### 6.1 AutoCAD local tools
- `get_active_document_context`
  - Class: `read`
  - Returns drawing metadata, units, active layout, layer summary, and selection context.
- `extract_geometry_delta`
  - Class: `read`
  - Returns debounced changed entities and a new `geometry_version_id`.
- `extract_geometry_snapshot`
  - Class: `read`
  - Returns a full or scoped geometry snapshot for validation or reporting.
- `focus_entities`
  - Class: `write`
  - Selects and zooms to one or more entity references.
- `highlight_violations`
  - Class: `write`
  - Paints violation overlays and annotation badges in the canvas.
- `clear_highlights`
  - Class: `write`
  - Removes active overlays and annotation state.
- `capture_annotated_view`
  - Class: `compute`
  - Captures a viewport image with optional annotation overlays.
- `preview_geometry_patch`
  - Class: `compute`
  - Simulates a proposed geometry edit and returns expected impact.
- `apply_geometry_patch`
  - Class: `write`
  - Applies a bounded CAD mutation after approval.

### 6.2 Validation and guardrail tools
- `run_validation`
  - Class: `compute`
  - Evaluates geometry against the active rule pack and returns `run_id`, categories, and violations.
- `get_guardrail_status`
  - Class: `read`
  - Returns latest pass or fail state by category and severity.
- `get_validation_run`
  - Class: `read`
  - Returns a full run snapshot including violations and provenance.
- `subscribe_guardrail_updates`
  - Class: `write`
  - Registers the workspace for push or poll-based run updates.

### 6.3 Canvas and entity resolution tools
- `resolve_entity_refs`
  - Class: `compute`
  - Remaps stored entity references to the current geometry version and returns confidence metadata.

### 6.4 Recommendation tools
- `get_violation_context`
  - Class: `read`
  - Returns violation, rule, geometry, and issue context for one or more findings.
- `get_rule_rationale`
  - Class: `read`
  - Returns human and machine-readable rule explanation and thresholds.
- `generate_fix_recommendations`
  - Class: `compute`
  - Produces ranked deterministic and AI-assisted fix options.
- `apply_safe_fix`
  - Class: `write`
  - Converts an approved recommendation into a bounded geometry patch.

### 6.5 Rule governance tools
- `list_rule_packs`
  - Class: `read`
  - Lists active, draft, approved, and published rule packs.
- `validate_rule_definition`
  - Class: `compute`
  - Validates rule schema and semantics before simulation.
- `simulate_rule_pack`
  - Class: `compute`
  - Runs a candidate rule pack against baseline samples or current geometry.
- `approve_rule_pack`
  - Class: `admin`
  - Records reviewer approval for a draft pack.
- `publish_rule_pack`
  - Class: `admin`
  - Publishes an approved rule pack and updates the active pointer.
- `rollback_rule_pack`
  - Class: `admin`
  - Repoints activation to a previous approved pack.

### 6.6 Reporting tools
- `get_report_input_bundle`
  - Class: `read`
  - Freezes the source data for report generation.
- `compute_compliance_score`
  - Class: `compute`
  - Produces weighted scores and category breakdowns.
- `generate_validation_report`
  - Class: `compute`
  - Builds canonical JSON and human-readable outputs such as PDF.
- `get_report_status`
  - Class: `read`
  - Returns job state and artifact references.

### 6.7 Collaboration tools
- `create_issue`
  - Class: `write`
  - Creates an issue from a violation or annotation.
- `assign_issue`
  - Class: `write`
  - Sets assignee and ownership metadata.
- `add_issue_comment`
  - Class: `write`
  - Adds discussion entries with mentions.
- `transition_issue_state`
  - Class: `write`
  - Moves issues across the workflow state machine.
- `verify_issue_fix`
  - Class: `compute`
  - Confirms whether the latest run resolves the linked finding.
- `notify_issue_watchers`
  - Class: `write`
  - Sends in-app, webhook, or email notifications.

### 6.8 Risk tools
- `extract_risk_features`
  - Class: `compute`
  - Produces model-ready features from geometry and validation context.
- `infer_design_risk`
  - Class: `compute`
  - Returns risk score, tier, confidence, and top factors.
- `explain_risk_score`
  - Class: `read`
  - Expands user-facing rationale for a risk prediction.

### 6.9 Blender conversion tools
- `export_scene_graph_ir`
  - Class: `compute`
  - Converts CAD structure into a canonical scene graph representation.
- `tessellate_geometry`
  - Class: `compute`
  - Produces mesh outputs and topology quality metrics.
- `enrich_scene_semantics`
  - Class: `compute`
  - Adds optional AI labels and hierarchy hints.
- `start_blender_conversion`
  - Class: `compute`
  - Creates an asynchronous conversion job.
- `get_conversion_status`
  - Class: `read`
  - Returns progress and artifact links.
- `get_conversion_quality_report`
  - Class: `read`
  - Returns unit, hierarchy, and fidelity metrics.

## 7. Feature-to-tool mapping
- Feature 01 depends on geometry extraction, validation, and guardrail status tools.
- Feature 02 depends on violation retrieval, entity resolution, highlight, focus, and screenshot tools.
- Feature 03 depends on violation context, rule rationale, recommendation generation, preview, and safe apply tools.
- Feature 04 depends on rule validation, simulation, approval, publish, and rollback tools.
- Feature 05 depends on frozen report bundle, screenshot, scoring, and report generation tools.
- Feature 06 depends on risk feature extraction, inference, and explanation tools.
- Feature 07 depends on issue creation, workflow transition, verification, and notification tools.
- Feature 08 depends on scene export, tessellation, semantic enrichment, and conversion job tools.

## 8. Recommended implementation stack
### 8.1 AutoCAD side
- .NET 8
- WPF for the unified workspace
- AutoCAD managed API for geometry extraction, highlighting, and safe edits

### 8.2 Backend side
- FastAPI for service endpoints and orchestration APIs
- Pydantic models as the source of truth for tool schemas and service DTOs
- PydanticAI as the recommended first agent runtime because it fits the current Python backend and typed tool design

### 8.3 Optional later additions
- LangGraph if long-running workflows, resumable execution, or approval-heavy agent graphs become necessary
- Provider adapters for Groq, xAI, and OpenAI behind one backend interface
- WebSocket push once the MVP polling path is stable

## 9. Repo impact and cleanup guidance
Files to rework:
- Move direct LLM invocation out of the WPF control and into the backend orchestrator.
- Merge the assistant and guardrail views into one workspace surface.
- Convert current free-form JSON action parsing into typed tool execution.

Files and patterns that appear to be legacy or low-value:
- `myCommands.resx` and `myCommands.Designer.cs` look like template leftovers unless they are reintroduced for localization.
- `SimulateAiResponse` in the chat control appears to be test scaffolding.
- An empty `PluginExtension` should become the startup composition root or be simplified.
- A hard-coded API key in the UI must be removed in favor of configuration.
- `KeepStraight` should be retained only if it remains part of the product roadmap; otherwise it should move into a legacy sample path.

## 10. Delivery plan
### Phase 1
- Stabilize Feature 01 as the deterministic backbone.
- Build the local AutoCAD tool host.
- Move LLM orchestration into the Python backend.
- Introduce a unified workspace shell while preserving old commands as aliases.

### Phase 2
- Add Feature 02 and 03 tool flows.
- Support annotation-driven issue context and recommendation cards.
- Add preview and approved-apply flows for safe edits.

### Phase 3
- Add Feature 04, 05, and 07 governance flows.
- Introduce immutable rule packs, report generation, and issue workflow state.

### Phase 4
- Add Feature 06 predictive risk and Feature 08 Blender conversion as asynchronous services.
- Keep both behind feature flags until evaluation data and operational guardrails are ready.

## 11. Definition of done for the agent platform
- The user can open one workspace instead of separate assistant and guardrail palettes.
- Geometry edits automatically produce validation results without prompting the agent.
- The agent can explain findings, highlight the right entities, recommend fixes, and create issues through typed tool calls.
- High-risk or state-changing actions require explicit approval and produce audit entries.
- Every downstream feature consumes the same canonical session, run, and violation contracts.
