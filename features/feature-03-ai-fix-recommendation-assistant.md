# Feature 03 - AI Fix Recommendation Assistant

## 1. Feature overview
The AI Fix Recommendation Assistant proposes practical corrective actions for each violation in plain language, including confidence and expected impact on manufacturability, safety, and downstream validation score.

Why it matters:
- Engineers need guidance, not only detection.
- Reduces trial-and-error loops and accelerates convergence to compliant designs.

User problem solved:
- Teams spend time interpreting standards and deciding which geometric adjustment is safest.

## 2. System role
Where it sits:
- Decision support layer between violation detection and engineer action.
- Consumer of rule metadata, geometry context, and historical fix outcomes.

Depends on:
- Feature 01 and 02 for violation context and geometry localization.
- Feature 04 rule authoring metadata for rule rationale and thresholds.
- Optional historical issue resolution data from Feature 07.

Features depending on it:
- Feature 07 collaboration uses recommendation cards in issue threads.
- Feature 05 reports include recommendation and confidence snapshots.
- Feature 06 can blend predicted risk with suggested interventions.

## 3. Architecture design
Assumptions:
- Enterprise policy allows internal LLM usage with strict data governance.
- Rule library contains machine-readable remediation guidance for many rules.

Core components:
- Recommendation Orchestrator: gathers context and calls deterministic plus AI paths.
- Retrieval Layer: fetches rule rationale, prior approved fixes, and design standards.
- Deterministic Heuristic Engine: proposes bounded parameter changes for known rule types.
- LLM Inference Service: generates ranked fix options with rationale.
- Confidence Calibrator: combines model score, heuristic agreement, and historical outcome.
- Recommendation API and UI Card Module: serves and displays ranked suggestions.
- Feedback Collector: captures accepted, edited, rejected outcomes for learning loop.

APIs and events:
- `POST /violations/{id}/recommendations:generate`
- `GET /violations/{id}/recommendations`
- Event `recommendation.feedback.recorded`

Data flow input to output:
1. User opens a violation from panel or annotation.
2. Orchestrator fetches violation, entity geometry features, and active rule pack context.
3. Heuristic engine produces deterministic candidate deltas.
4. Retrieval and LLM generate additional options with rationale.
5. Calibrator scores confidence and predicted impact.
6. UI presents ranked fixes with expected effect and safety notes.
7. User action feedback is persisted for model and policy refinement.

## 4. Detailed build plan
Work split:
- AI engineering team: retrieval pipeline, prompt templates, calibrator, offline evaluation.
- Backend team: orchestration APIs, data contracts, caching, policy controls.
- Frontend team: recommendation cards, comparison view, apply-suggestion workflow hooks.
- Data engineering: feedback dataset and feature store for confidence calibration.
- Security and compliance: redaction policies and model access controls.

Integration contracts:
- `RecommendationContext` contract from validation domain to AI domain.
- `RecommendationOption` schema with `confidence`, `expected_impact`, and `guardrails`.
- Feedback contract with explicit user intent outcome categories.

Implementation order:
1. Start with deterministic rule-specific fixes for top violation types.
2. Add retrieval-backed LLM suggestions with strict output schema validation.
3. Add confidence calibration and quality thresholding.
4. Add feedback loop and periodic model quality review.
5. Integrate one-click action helpers where CAD API supports safe edits.

## 5. Data model and schema considerations
Core entities:
- `RecommendationSet(set_id, violation_id, model_version, generated_at)`
- `RecommendationOption(option_id, set_id, rank, text, parameter_patch_json, confidence, expected_impact_json)`
- `RecommendationFeedback(feedback_id, option_id, user_id, outcome, notes, recorded_at)`
- `ModelEvaluationSnapshot(snapshot_id, model_version, precision_at_1, acceptance_rate, created_at)`

Relationships:
- One violation may have multiple recommendation sets over time.
- One set has many ranked options.
- Feedback links option quality to eventual issue resolution.

Versioning strategy:
- Immutable recommendation sets tied to model version and prompt template version.
- Keep both heuristic and LLM provenance fields.

Auditability and traceability:
- Persist context hash and retrieval document IDs.
- Store policy checks and blocked output reasons.

## 6. Validation and observability
Testing strategy:
- Offline benchmark against curated violation and fix corpus.
- Schema validation tests for all model responses.
- Human-in-the-loop review for high-impact rule categories.

Logging and monitoring:
- Metrics: recommendation latency, acceptance rate, confidence calibration error.
- Alert when hallucination guardrails trigger above threshold.

Failure handling and recovery:
- If AI path fails, return deterministic recommendations only.
- If confidence is below threshold, label recommendation as `needs review`.

## 7. Risks and edge cases
Technical risks:
- Hallucinated recommendations that conflict with standards.
- Prompt drift after rule pack changes.

Product risks:
- Over-reliance on AI can reduce engineer critical review.

Performance bottlenecks:
- Inference latency under peak interactive usage.

Data quality issues:
- Sparse feedback data in early rollout causes weak calibration.

Human workflow risks:
- Engineers may reject recommendations without giving feedback, reducing learning signal.

## 8. MVP vs production roadmap
MVP first:
- Deterministic recommendations for top 10 frequent rule failures.
- Optional AI suggestions behind feature flag.
- Manual feedback capture with simple accept or reject.

Deferred:
- Automatic CAD edit patching.
- Personalization by engineer or product family.

Production maturity:
- Continuous calibration pipeline with shadow evaluation.
- Policy-gated enterprise model deployment with safe fallback tiers.

## 9. Final recommendation
Recommended approach:
- Hybrid recommendation architecture: deterministic first, AI second, calibration always.

Tradeoffs considered:
- Pure LLM generation gives coverage but lower reliability.
- Pure deterministic rules are reliable but narrow.

Why this fit is best:
- Hybrid mode delivers immediate value with safety while steadily improving through feedback and model evolution.

## 10. Agent implementation case scenarios
Scenario A - Deterministic fix available for known rule type:
- Trigger: violation rule ID exists in heuristic mapping table.
- Build exactly: generate at least one deterministic `RecommendationOption` with bounded parameter patch and estimated validation impact.
- Contract details: `RecommendationOption` includes `option_id`, `source_type`, `parameter_patch_json`, `confidence`, `expected_impact_json`, `safety_flags`.
- Done criteria: deterministic option can be returned even when AI services are unavailable.

Scenario B - AI and deterministic recommendations disagree:
- Trigger: top AI recommendation conflicts with deterministic safety guardrail.
- Build exactly: rank deterministic option above conflicting AI option and add explanation note with policy reason code.
- Contract details: response includes `rank_explanation`, `policy_decision`, `blocked_fields`.
- Done criteria: policy-violating options are never ranked first.

Scenario C - Low-confidence AI output:
- Trigger: calibrated confidence below release threshold.
- Build exactly: label option `needs_review`, hide one-click apply action, and request explicit user confirmation.
- Contract details: thresholds are versioned in `model_policy_version` and persisted per recommendation set.
- Done criteria: unsafe or uncertain AI output cannot trigger automated geometry mutation.

## 11. Agent orchestration and tool calls
This feature is the first place where the agent moves from explanation into action planning, so it must use typed tools from [Feature 00 - Unified Agentic Compliance Architecture](./feature-00-unified-agentic-compliance-architecture.md) instead of parsing free-form JSON from chat output.

Required tool calls:
- `get_violation_context`
- `get_rule_rationale`
- `generate_fix_recommendations`
- `preview_geometry_patch`
- `apply_safe_fix`

Future supporting tool calls:
- `get_guardrail_status`
- `verify_issue_fix`

Agent orchestration notes:
- The agent can generate and rank recommendations automatically after a failing run.
- Preview flows may run automatically, but `apply_safe_fix` must require user approval unless an explicit safe automation policy exists.
- Recommendation provenance should always include whether the option came from deterministic heuristics, AI generation, or a blended ranking path.

