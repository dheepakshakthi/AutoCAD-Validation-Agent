# Feature 06 - Risk Prediction Before Finalization

## 1. Feature overview
Risk Prediction Before Finalization scores geometry regions and assemblies for probable downstream failure or rework risk even when deterministic rules have not yet failed.

Why it matters:
- Deterministic validation catches known rule violations but misses latent risk patterns.
- Predictive guidance reduces late-stage design surprises and manufacturing defects.

User problem solved:
- Teams need early warning for risky design patterns not explicitly encoded in current rule sets.

## 2. System role
Where it sits:
- Predictive intelligence layer parallel to deterministic rule engine.
- Injects proactive risk signals into panel, canvas overlays, and reports.

Depends on:
- Historical design and defect outcomes.
- Feature extraction from CAD geometry and validation outputs.
- Model registry and inference serving stack.

Features depending on it:
- Feature 01 panel displays blended risk and compliance status.
- Feature 02 highlights high-risk regions.
- Feature 03 uses risk priors to rank recommendation options.
- Feature 05 reports include risk trend and confidence.

## 3. Architecture design
Assumptions:
- Historical labeled outcomes are available from manufacturing or QA systems.
- Inference latency target is under 3 seconds for interactive relevance.

Core components:
- Feature Extractor Service: transforms geometry and validation state into ML features.
- Training Pipeline: builds datasets, trains models, validates performance, and registers versions.
- Online Inference Service: serves per-run or per-region risk scores.
- Risk Calibration Service: maps raw probabilities to user-facing confidence bands.
- Drift Monitor: detects feature and label drift.
- Risk API and UI adapters: expose and render risk insights.

APIs and event flows:
- Event `validation.completed` triggers feature extraction and inference.
- `POST /risk/infer` with run context and scope.
- `GET /risk/scores?runId=...` for panel and report consumption.

Data flow input to output:
1. Validation run finalizes with geometry and violation context.
2. Feature extractor computes structured feature vector.
3. Inference service returns probability and explanatory factors.
4. Calibrator converts score into risk tier and confidence.
5. UI and report services consume risk payloads.
6. Outcomes feed back to model retraining datasets.

## 4. Detailed build plan
Work split:
- ML team: feature definitions, model training, calibration, and drift strategy.
- Data engineering: historical data ingestion and labeling pipelines.
- Backend team: inference API, model routing, and caching.
- Frontend team: risk visualization and explanation UI.
- DevOps and MLOps: model deployment, canary, rollback, and governance.

Integration contracts:
- `RiskFeatureVector` schema versioned in feature store.
- `RiskScore` response contract with `score`, `tier`, `confidence`, and `explanations`.
- `OutcomeLabel` ingestion contract from QA and manufacturing systems.

Implementation order:
1. Build baseline model offline with limited features.
2. Deploy shadow inference with no user-visible output.
3. Evaluate calibration against real outcomes.
4. Enable user-visible risk tiers in panel for pilot projects.
5. Expand to region-level heatmaps and report integration.

## 5. Data model and schema considerations
Core entities:
- `RiskModelVersion(model_version, feature_schema_version, trained_at, status)`
- `RiskInference(inference_id, run_id, model_version, score, tier, confidence, created_at)`
- `RiskExplanation(explanation_id, inference_id, factor, contribution)`
- `OutcomeLabel(label_id, design_id, outcome_type, severity, observed_at)`
- `TrainingDatasetVersion(dataset_version, feature_schema_version, label_window, created_at)`

Relationships:
- One inference ties to one model version and one validation run.
- Many explanations can belong to one inference.

Versioning strategy:
- Version model, feature schema, and dataset independently.
- Block promotion when schema compatibility checks fail.

Auditability and traceability:
- Persist training data lineage and model artifact checksum.
- Keep inference request payload hash for reproducibility.

## 6. Validation and observability
Testing strategy:
- Offline metrics: precision recall AUC calibration error.
- Online shadow comparison before user exposure.
- Backtesting against prior project timelines.

Logging and monitoring:
- Metrics: inference latency, drift score, calibration error, false positive rate.
- Alerts when drift exceeds threshold or latency breaches SLO.

Failure handling and recovery:
- If inference fails, system falls back to deterministic validation only.
- Automatic rollback to previous model version on degraded quality gates.

## 7. Risks and edge cases
Technical risks:
- Label leakage from post-release data can inflate offline performance.
- Distribution shift across product families harms generalization.

Product risks:
- Users may over-trust predicted risk and under-review deterministic violations.

Performance bottlenecks:
- High-dimensional feature extraction for large assemblies.

Data quality issues:
- Incomplete defect linkage to design versions reduces training signal quality.

Human workflow risks:
- Risk score without explanation can reduce user adoption.

## 8. MVP vs production roadmap
MVP first:
- Assembly-level risk score with simple explanatory factors.
- Pilot on one product family with shadow and gated rollout.

Deferred:
- Per-feature geometric risk heatmaps.
- Auto-prioritization of collaboration tasks by predicted risk.

Production maturity:
- Continuous training and monitoring with human-in-the-loop review.
- Multi-family models with transfer-learning or model routing.

## 9. Final recommendation
Recommended approach:
- Introduce predictive risk as a calibrated supplement to deterministic validation, not a replacement.

Tradeoffs considered:
- End-to-end deep models may improve accuracy but reduce interpretability.
- Gradient boosting plus engineered features is easier to explain and operationalize first.

Why this fit is best:
- It produces actionable early warnings while preserving transparency and enterprise trust.

## 10. Agent implementation case scenarios
Scenario A - Shadow inference before user exposure:
- Trigger: `validation.completed` event in pilot projects.
- Build exactly: run inference, persist `RiskInference`, but suppress UI display and compare predictions against later `OutcomeLabel` records.
- Contract details: `RiskInference` carries `visibility_mode` with values `shadow` or `visible`.
- Done criteria: shadow quality report is available per model version before rollout gate.

Scenario B - Drift spike in production:
- Trigger: feature drift or calibration error crosses threshold.
- Build exactly: auto-switch traffic to previous stable model and downgrade visible confidence tier until retraining completes.
- Contract details: drift alerts include `model_version`, `feature_schema_version`, `drift_metric`, `threshold`.
- Done criteria: rollback occurs without API downtime and is visible in model audit logs.

Scenario C - Incomplete or delayed labels:
- Trigger: outcome labels from manufacturing arrive late.
- Build exactly: training pipeline uses label windows and excludes not-yet-mature samples from training sets.
- Contract details: `OutcomeLabel` includes `label_source`, `observed_at`, `label_maturity_state`.
- Done criteria: model training jobs are reproducible and free from label leakage.

## 11. Agent orchestration and tool calls
This feature should be layered into the same agent runtime defined in [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md) as a supplemental intelligence path, not a replacement for deterministic validation.

Required tool calls:
- `extract_risk_features`
- `infer_design_risk`
- `explain_risk_score`

Future supporting tool calls:
- `get_guardrail_status`
- `generate_fix_recommendations`

Agent orchestration notes:
- Risk signals should enrich guardrail prioritization and recommendation ranking, but never suppress concrete rule failures.
- The agent may surface risk after validation completion or when the user requests a pre-finalization review.
- Risk outputs should be traceable to model version, feature schema version, and calibration policy.

