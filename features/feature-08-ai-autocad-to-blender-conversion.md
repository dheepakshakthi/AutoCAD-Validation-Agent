# Feature 08 - AI AutoCAD to Blender Conversion

## 1. Feature overview
This feature converts AutoCAD design structures into Blender-compatible 3D structures using a hybrid geometry conversion and AI-assisted semantic mapping pipeline.

Why it matters:
- Teams can reuse engineering CAD designs for visualization, simulation preparation, training content, and digital twin workflows in Blender.
- Reduces manual remodeling effort and preserves design intent across tools.

User problem solved:
- Direct CAD-to-DCC transfer often loses hierarchy, materials, constraints, and semantics.

## 2. System role
Where it sits:
- Cross-platform interoperability layer extending the core CAD intelligence platform.
- Uses existing geometry versions and metadata, then publishes Blender-ready assets and import manifests.

Depends on:
- Geometry snapshot pipeline from Feature 01 infrastructure.
- Annotation and issue metadata if optional compliance overlays are exported.
- Asset storage, job orchestration, and AI inference services.

Features depending on it:
- Visualization teams and downstream Blender workflows.
- Feature 05 reports can attach Blender preview artifacts.
- Feature 06 can leverage converted assets for simulation-oriented risk review.

## 3. Architecture design
Assumptions:
- AutoCAD exports are available as DWG, DXF, or B-Rep extracts via plugin adapters.
- Blender import target supports glTF, USD, or native Python import scripts.

Core components:
- Export Adapter: extracts geometry, layers, metadata, and transforms from AutoCAD.
- Canonical Scene Graph IR Service: normalizes CAD structure into tool-neutral representation.
- Geometry Tessellation and Topology Service: converts B-Rep and NURBS to mesh with quality controls.
- AI Semantic Mapper: predicts object class, material hints, and hierarchy naming for Blender scene.
- Blender Package Builder: generates glTF or USD plus Blender import manifest.
- Conversion Worker Queue: asynchronous processing for large assemblies.
- Validation Bridge: optional transfer of compliance annotations into Blender overlays.

APIs and event flows:
- `POST /conversion-jobs` with source design version and export options.
- `GET /conversion-jobs/{id}` status and quality metrics.
- Event `conversion.completed` with artifact references.

Data flow input to output:
1. User requests conversion for a design version.
2. Export adapter captures CAD structure and metadata.
3. Scene graph IR is generated and validated.
4. Tessellation service creates mesh LOD outputs.
5. AI mapper enriches object labels and hierarchy.
6. Blender package builder emits artifacts and import script.
7. Artifacts are stored and linked back to design session.

## 4. Detailed build plan
Work split:
- CAD integration team: robust extraction adapters and geometry fidelity tests.
- Graphics and pipeline team: IR schema, tessellation, and export packaging.
- AI team: semantic mapping model and confidence thresholds.
- Backend platform team: job orchestration API, queue workers, and artifact lifecycle.
- DevOps team: GPU or CPU worker autoscaling and storage cost controls.

Integration contracts:
- `SceneGraphIR` schema shared by extraction, AI, and export layers.
- `ConversionQualityReport` contract with mesh error and hierarchy preservation metrics.
- `BlenderManifest` contract for deterministic import behavior.

Implementation order:
1. Build deterministic conversion without AI enrichment.
2. Establish quality metrics and baseline acceptance thresholds.
3. Add AI semantic mapper behind feature flag.
4. Add optional compliance overlay export.
5. Harden for large assemblies with async processing and retries.

## 5. Data model and schema considerations
Core entities:
- `ConversionJob(job_id, project_id, source_design_version, status, requested_by, created_at)`
- `SceneGraphNode(node_id, job_id, parent_node_id, source_ref, transform_json, metadata_json)`
- `MeshArtifact(mesh_id, job_id, format, lod, object_key, checksum)`
- `SemanticTag(tag_id, node_id, label, confidence, model_version)`
- `ConversionQualityReport(report_id, job_id, topology_loss_score, unit_scale_error, hierarchy_match_score)`

Relationships:
- One conversion job has many scene graph nodes and mesh artifacts.
- Semantic tags attach to nodes and are versioned by model.

Versioning strategy:
- Scene graph and export manifest versions stored per job.
- AI model version pinned on semantic tags.

Auditability and traceability:
- Preserve source design and geometry version references.
- Persist conversion configuration for reproducibility.

## 6. Validation and observability
Testing strategy:
- Golden-file geometry fidelity tests.
- Topology and transform consistency checks.
- Blender import integration tests with automated scene validation.

Logging and monitoring:
- Metrics: conversion duration, mesh error rates, failed imports, AI confidence distribution.
- Alerts: job retry spikes, quality score drops, storage growth anomalies.

Failure handling and recovery:
- Job-level retries for transient converter failures.
- Fallback deterministic naming when AI confidence is below threshold.
- Partial artifact rollback if package integrity checks fail.

## 7. Risks and edge cases
Technical risks:
- Precision loss in tessellation for complex curved surfaces.
- Unit and coordinate system mismatches.

Product risks:
- Users may expect exact CAD parametric behavior in Blender, which is not always feasible.

Performance bottlenecks:
- Very large assemblies can cause high memory pressure in conversion workers.

Data quality issues:
- Incomplete material or layer metadata from source files.

Human workflow risks:
- Users may bypass quality report and consume low-fidelity exports.

## 8. MVP vs production roadmap
MVP first:
- Deterministic conversion to glTF with hierarchy and basic material mapping.
- Async job tracking with downloadable artifact package.

Deferred:
- AI semantic enrichment and compliance overlay transfer.
- USD pipeline and advanced Blender automation scripts.

Production maturity:
- Multi-format export support, advanced quality tuning, and enterprise-scale job orchestration.
- Closed-loop feedback from Blender users to improve semantic mapping.

## 9. Final recommendation
Recommended approach:
- Start with deterministic scene graph conversion and add AI enrichment as a safe augmentation layer.

Tradeoffs considered:
- AI-first direct conversion can reduce manual naming work but increases unpredictability.
- Deterministic-first conversion ensures repeatability and easier debugging.

Why this fit is best:
- It preserves engineering trust, enables measurable quality gates, and still allows AI-driven improvement over time.

## 10. Agent implementation case scenarios
Scenario A - Deterministic conversion baseline:
- Trigger: user submits conversion job with AI enrichment disabled.
- Build exactly: generate `SceneGraphIR`, tessellate to target format, produce `BlenderManifest`, and publish artifact bundle with quality report.
- Contract details: `ConversionQualityReport` includes `topology_loss_score`, `hierarchy_match_score`, `unit_scale_error`, `missing_metadata_count`.
- Done criteria: artifact imports into supported Blender version without manual scene repair for pilot benchmark set.

Scenario B - Unit and axis mismatch handling:
- Trigger: source model uses millimeters and Z-up while Blender target expects meters and Z-up conventions with normalized transforms.
- Build exactly: apply deterministic unit conversion and coordinate normalization before mesh export; persist conversion matrix metadata.
- Contract details: manifest stores `source_unit`, `target_unit`, `unit_scale_factor`, `axis_mapping`, `global_transform_matrix`.
- Done criteria: imported scene bounding box and part distances match source tolerance threshold.

Scenario C - Low-confidence semantic enrichment:
- Trigger: AI semantic mapper confidence below configured threshold.
- Build exactly: keep deterministic names, mark semantic tag as tentative, and include review queue item for manual curation.
- Contract details: `SemanticTag` includes `confidence`, `model_version`, `status` (`accepted`, `tentative`, `rejected`).
- Done criteria: low-confidence AI output never overwrites deterministic baseline labels.

## 11. Agent orchestration and tool calls
This feature should plug into the same orchestration backbone defined in [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md), but it should remain asynchronous and clearly separated from the core compliance loop.

Required tool calls:
- `export_scene_graph_ir`
- `tessellate_geometry`
- `enrich_scene_semantics`
- `start_blender_conversion`
- `get_conversion_status`
- `get_conversion_quality_report`

Future supporting tool calls:
- `capture_annotated_view`
- `generate_validation_report`

Agent orchestration notes:
- Conversion must always consume a frozen geometry version so the job remains reproducible.
- The agent may recommend conversion after validation or reporting, but conversion should not block the main AutoCAD workflow.
- Low-confidence semantic enrichment must remain optional and must never overwrite deterministic conversion outputs silently.

