# Feature 02 - In-Canvas Issue Highlighting

## 1. Feature overview
In-Canvas Issue Highlighting renders rule violations directly on faces, edges, features, and component instances inside the CAD viewport, with clickable annotations that deep-link to issue details.

Why it matters:
- Converts abstract rule failures into spatially actionable context.
- Reduces time spent searching for the problematic geometry.

User problem solved:
- Engineers lose time mapping text-only violations to exact model locations.

## 2. System role
Where it sits:
- UI and interaction layer over the CAD canvas.
- Synchronization bridge between validation output and geometry topology.

Depends on:
- Feature 01 guardrail outputs and violation metadata.
- CAD API hooks for overlay rendering, entity picking, and camera navigation.
- Geometry reference resolver for topology changes.

Features depending on it:
- Feature 03 recommendation assistant (contextual recommendation launch point).
- Feature 07 collaboration workflow (issue creation from selected annotation).
- Feature 05 reporting (annotated screenshots and evidence capture).

## 3. Architecture design
Assumptions:
- CAD environment supports custom visual overlays and click callbacks.
- Violation payload includes canonical `entity_ref` values.

Core components:
- Overlay Renderer Module: color coding by severity and category.
- Annotation Controller: hover card, click interactions, issue drill-down.
- Geometry Reference Resolver Service: maps stored refs to current topology after edits.
- Selection Sync Service: keeps panel selection and viewport annotation in sync.
- Screenshot Capture Adapter: captures annotated view for reports.

APIs and events:
- `GET /validation-runs/{runId}/violations?includeEntityRefs=true`
- `POST /entity-ref/resolve` to remap stale refs.
- Event `annotation.clicked` for recommendation and collaboration workflows.

Data flow input to output:
1. Guardrail update arrives with violation IDs.
2. Client fetches violation details and refs.
3. Resolver checks current geometry version and remaps if needed.
4. Overlay renderer draws highlights with severity palette.
5. User clicks annotation to open violation card and actions.

## 4. Detailed build plan
Work split:
- Frontend CAD team: rendering pipeline, interaction state, performance culling.
- Backend geometry team: entity ref resolver and topology remap logic.
- Platform team: APIs for violation hydration and resolver access.
- QA team: deterministic visual test scenes and topology mutation suites.

Integration contracts:
- `EntityRef` schema contract between rule engine and CAD renderer.
- `ResolvedEntityRef` response contract with confidence level.
- `AnnotationActionEvent` contract for recommendation and collaboration triggers.

Implementation order:
1. Define stable entity reference format.
2. Build static overlay rendering for known refs.
3. Add click interactions and panel sync.
4. Implement resolver for topology drift.
5. Add screenshot capture and report integration.
6. Optimize rendering for large assemblies.

## 5. Data model and schema considerations
Core entities:
- `Annotation(annotation_id, violation_id, geometry_version_id, entity_ref, render_state)`
- `EntityRefResolution(resolution_id, source_ref, target_ref, confidence, resolved_at)`
- `ViewportSnapshot(snapshot_id, run_id, annotation_ids_json, object_key)`

Relationships:
- One violation can have multiple annotations if issue spans multiple entities.
- One annotation can have many resolution attempts across versions.

Versioning strategy:
- Annotation is version-bound to geometry version.
- Resolver emits new mapping records instead of mutating old refs.

Auditability and traceability:
- Persist each ref remap with algorithm version and confidence.
- Save screenshot hash to detect accidental overwrite.

## 6. Validation and observability
Testing strategy:
- Visual regression tests across standard assemblies.
- Property-based tests for resolver behavior under topology changes.
- Interaction tests for click, hover, and cross-panel synchronization.

Logging and monitoring:
- Metrics: overlay render time, resolver success rate, unresolved ref rate.
- Structured logs with `run_id`, `violation_id`, and `entity_ref`.

Failure handling and recovery:
- If ref resolution fails, show violation in list with `location unavailable` state.
- Defer heavy remap requests to async queue under high load.

## 7. Risks and edge cases
Technical risks:
- Unstable topology IDs during aggressive feature edits.
- Overlay performance degradation on dense assemblies.

Product risks:
- Misleading highlights can reduce trust in the platform.

Performance bottlenecks:
- Repeated resolver calls during rapid edit streams.

Data quality issues:
- Partial entity refs from rule workers reduce mapping precision.

Human workflow risks:
- Users can focus only on visible issues and miss hidden component violations.

## 8. MVP vs production roadmap
MVP first:
- Highlight primary violating entity only.
- Manual ref refresh after major topology edits.
- Basic click-to-open violation behavior.

Deferred:
- Multi-entity gradient overlays.
- Real-time topology remap with confidence visualization.

Production maturity:
- Spatial indexing and viewport-aware rendering culling.
- Deterministic screenshot evidence pipeline for reports.

## 9. Final recommendation
Recommended approach:
- Use a stable canonical `EntityRef` contract plus a dedicated resolver service to absorb topology drift.

Tradeoffs considered:
- Direct CAD native IDs are fast but brittle across edits.
- Canonical refs with resolver add complexity but preserve cross-run traceability.

Why this fit is best:
- It keeps issue localization reliable, which is mandatory for user trust and for downstream report and collaboration workflows.

## 10. Agent implementation case scenarios
Scenario A - Normal highlight rendering for stable topology:
- Trigger: run completes and `entity_ref` maps directly to current `geometry_version_id`.
- Build exactly: draw severity color overlay, attach `violation_id` metadata, and register click callback with deep link payload.
- Contract details: `EntityRef` must include `part_path`, `topology_token`, `primitive_type`, and `feature_hint`.
- Done criteria: click on overlay opens correct violation in under 500 milliseconds on benchmark assembly.

Scenario B - Topology drift after geometry edits:
- Trigger: stored `entity_ref` cannot be resolved for current version.
- Build exactly: call resolver, store append-only `EntityRefResolution` row, and render with confidence badge.
- Contract details: resolver response includes `resolved_ref`, `confidence`, `resolver_version`, `reason_code`.
- Done criteria: unresolved mappings are explicitly marked and never shown as precise highlights.

Scenario C - Dense models causing overlay performance degradation:
- Trigger: frame time exceeds target budget while rendering many violations.
- Build exactly: enable viewport culling, cluster low-severity markers at distance, and lazily hydrate tooltip details.
- Contract details: renderer telemetry emits `frame_time_ms`, `overlay_count`, `culled_count`, `camera_distance_bucket`.
- Done criteria: frame-time SLO remains within target for pilot benchmark models.

## 11. Agent orchestration and tool calls
This feature should be implemented as a tool-driven extension of [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md), not as a standalone rendering subsystem disconnected from the agent.

Required tool calls:
- `get_validation_run`
- `highlight_violations`
- `clear_highlights`
- `focus_entities`
- `capture_annotated_view`

Future supporting tool calls:
- `resolve_entity_refs`
- `create_issue`

Agent orchestration notes:
- The agent should automatically highlight newly selected or high-severity findings after validation completes.
- Highlighting and focus actions may run without confirmation because they do not alter model geometry.
- Annotation click-through should feed issue creation and recommendation generation rather than open a disconnected workflow.

