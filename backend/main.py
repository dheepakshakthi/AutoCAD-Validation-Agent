"""AutoCAD Validation Agent backend with run-oriented Feature 00/01 contracts."""

from datetime import datetime, timezone
import json
import os
from threading import Lock
import urllib.error
import urllib.request
import uuid

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from models import (
    ActiveDocumentContextRequest,
    ActiveDocumentContextResponse,
    AgentChatRequest,
    AgentChatResponse,
    ApprovalDecisionRequest,
    ApprovalItem,
    CreateIssueRequest,
    GenerateReportRequest,
    GeometryPayload,
    GuardrailStatusDTO,
    GuardrailSubscriptionRequest,
    GuardrailSubscriptionResponse,
    IssueRecord,
    RecommendationBundle,
    RecommendationOption,
    ReportRecord,
    ProjectConstraintsConfigRequest,
    ProjectConstraintsResponse,
    RuleListResponse,
    TransitionIssueStateRequest,
    UnifiedAgentQueryRequest,
    UnifiedAgentResponse,
    ValidationCategory,
    ValidationConstraints,
    ValidationRequest,
    ValidationResponse,
    ValidationRunRecord,
    Violation,
)
from rule_engine import RuleEngine

load_dotenv()

app = FastAPI(
    title="AutoCAD Validation Agent",
    description="Deterministic design compliance service with typed tool contracts",
    version="0.2.0",
)

# CORS configuration for AutoCAD plugin.
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

rule_engine = RuleEngine()
store_lock = Lock()

validation_runs: dict[str, ValidationRunRecord] = {}
latest_run_by_session: dict[tuple[str, str], str] = {}
active_run_by_session: dict[tuple[str, str], str] = {}

issues: dict[str, IssueRecord] = {}
issue_index_by_session: dict[tuple[str, str], list[str]] = {}

reports: dict[str, ReportRecord] = {}
report_index_by_session: dict[tuple[str, str], list[str]] = {}

approvals: dict[str, ApprovalItem] = {}
approval_index_by_session: dict[tuple[str, str], list[str]] = {}

constraint_profiles: dict[tuple[str, str], ProjectConstraintsResponse] = {}

WORKER_BUILD_HASH = "worker-local-dev"
DEFAULT_POLL_INTERVAL_SECONDS = 2
GROQ_API_KEY = os.getenv("GROQ_API_KEY", "").strip()
GROQ_MODEL = os.getenv("GROQ_MODEL", "qwen/qwen3-32b").strip()


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _empty_severity_counts() -> dict[str, int]:
    return {"high": 0, "medium": 0, "low": 0}


def _compute_category_state(violations) -> list[ValidationCategory]:
    category_state: dict[str, ValidationCategory] = {
        name: ValidationCategory(name=name, status="pass", severity_counts=_empty_severity_counts())
        for name in rule_engine.get_categories()
    }

    for violation in violations:
        if violation.category not in category_state:
            category_state[violation.category] = ValidationCategory(
                name=violation.category,
                status="pass",
                severity_counts=_empty_severity_counts(),
            )

        category = category_state[violation.category]
        category.status = "fail"
        category.severity_counts[violation.severity] = category.severity_counts.get(violation.severity, 0) + 1

    return sorted(category_state.values(), key=lambda item: item.name)


def _aggregate_severity_counts(categories: list[ValidationCategory]) -> dict[str, int]:
    counts = _empty_severity_counts()
    for category in categories:
        counts["high"] += category.severity_counts.get("high", 0)
        counts["medium"] += category.severity_counts.get("medium", 0)
        counts["low"] += category.severity_counts.get("low", 0)
    return counts


def _session_key(project_id: str, session_id: str) -> tuple[str, str]:
    return (project_id, session_id)


def _constraint_scope_key(project_id: str, drawing_id: str) -> tuple[str, str]:
    normalized = drawing_id.strip() if drawing_id else ""
    if not normalized:
        normalized = "active-drawing"
    return (project_id, normalized)


def _default_constraint_profile(project_id: str, drawing_id: str) -> ProjectConstraintsResponse:
    return ProjectConstraintsResponse(
        project_id=project_id,
        drawing_id=drawing_id,
        profile_name="default-profile",
        source="default",
        constraints=ValidationConstraints(),
        configured_at=_utc_now(),
    )


def _resolve_constraint_profile(project_id: str, drawing_id: str) -> ProjectConstraintsResponse:
    key = _constraint_scope_key(project_id, drawing_id)
    with store_lock:
        existing = constraint_profiles.get(key)

    if existing:
        return existing

    return _default_constraint_profile(project_id=key[0], drawing_id=key[1])


def _upsert_constraint_profile(request: ProjectConstraintsConfigRequest) -> ProjectConstraintsResponse:
    key = _constraint_scope_key(request.project_id, request.drawing_id)
    profile_name = request.profile_name.strip() if request.profile_name else ""
    if not profile_name:
        profile_name = "custom-profile"

    profile = ProjectConstraintsResponse(
        project_id=key[0],
        drawing_id=key[1],
        profile_name=profile_name,
        source="custom",
        constraints=request.constraints,
        configured_at=_utc_now(),
    )

    with store_lock:
        constraint_profiles[key] = profile

    return profile


def _append_unique(index: dict[tuple[str, str], list[str]], key: tuple[str, str], value: str) -> None:
    bucket = index.setdefault(key, [])
    if value not in bucket:
        bucket.append(value)


def _list_session_issues(project_id: str, session_id: str) -> list[IssueRecord]:
    key = _session_key(project_id, session_id)
    with store_lock:
        ids = list(issue_index_by_session.get(key, []))
        return [issues[issue_id] for issue_id in ids if issue_id in issues]


def _list_session_reports(project_id: str, session_id: str) -> list[ReportRecord]:
    key = _session_key(project_id, session_id)
    with store_lock:
        ids = list(report_index_by_session.get(key, []))
        return [reports[report_id] for report_id in ids if report_id in reports]


def _list_session_approvals(project_id: str, session_id: str) -> list[ApprovalItem]:
    key = _session_key(project_id, session_id)
    with store_lock:
        ids = list(approval_index_by_session.get(key, []))
        return [approvals[approval_id] for approval_id in ids if approval_id in approvals]


def _compute_compliance_score(severity_counts: dict[str, int]) -> float:
    penalty = (
        severity_counts.get("high", 0) * 25
        + severity_counts.get("medium", 0) * 10
        + severity_counts.get("low", 0) * 3
    )
    return max(0.0, 100.0 - float(penalty))


def _create_issue_from_violation(
    run: ValidationRunRecord,
    violation: Violation,
    assignee: str | None = None,
) -> IssueRecord:
    issue_id = f"issue_{violation.id}"
    now = _utc_now()

    with store_lock:
        existing = issues.get(issue_id)
        if existing:
            return existing

        issue = IssueRecord(
            issue_id=issue_id,
            session_id=run.session_id,
            project_id=run.project_id,
            run_id=run.run_id,
            violation_id=violation.id,
            title=f"{violation.category}: {violation.message}",
            severity=violation.severity,
            status="open",
            assignee=assignee,
            created_at=now,
            updated_at=now,
        )
        issues[issue_id] = issue
        _append_unique(issue_index_by_session, _session_key(run.project_id, run.session_id), issue_id)

    return issue


def _generate_recommendations_for_run(run: ValidationRunRecord, limit: int = 6) -> list[RecommendationOption]:
    severity_rank = {"high": 0, "medium": 1, "low": 2}
    ordered_violations = sorted(run.violations, key=lambda item: severity_rank.get(item.severity, 3))
    options: list[RecommendationOption] = []

    for violation in ordered_violations[:limit]:
        category = violation.category.lower()
        if category == "dimensions":
            title = "Adjust geometry dimensions"
            rationale = "Modify radius or line length to satisfy the configured threshold for this rule."
        elif category == "layers":
            title = "Move entity to production layer"
            rationale = "Reassign the entity from layer 0 to a standards-compliant working layer."
        elif category == "text":
            title = "Increase annotation legibility"
            rationale = "Increase text height and keep annotation readable at drawing scale."
        else:
            title = "Review and correct geometry"
            rationale = "Apply a bounded edit to satisfy the failing rule, then re-run validation."

        confidence = 0.90 if violation.severity == "high" else 0.78 if violation.severity == "medium" else 0.65
        options.append(
            RecommendationOption(
                option_id=f"rec_{violation.id}",
                title=title,
                rationale=rationale,
                target_entity_ref=violation.entity_ref,
                confidence=confidence,
                requires_approval=violation.severity == "high",
            )
        )

    return options


def _upsert_report_for_run(run: ValidationRunRecord) -> ReportRecord:
    report_id = f"report_{run.run_id}"
    score = _compute_compliance_score(run.severity_counts)
    summary = (
        f"Run {run.run_id} status {run.status}. "
        f"Violations: {len(run.violations)}. "
        f"High {run.severity_counts.get('high', 0)}, "
        f"Medium {run.severity_counts.get('medium', 0)}, "
        f"Low {run.severity_counts.get('low', 0)}."
    )

    record = ReportRecord(
        report_id=report_id,
        session_id=run.session_id,
        project_id=run.project_id,
        run_id=run.run_id,
        status="generated",
        compliance_score=score,
        summary=summary,
        created_at=_utc_now(),
    )

    with store_lock:
        reports[report_id] = record
        _append_unique(report_index_by_session, _session_key(run.project_id, run.session_id), report_id)

    return record


def _sync_approvals_for_run(run: ValidationRunRecord) -> list[ApprovalItem]:
    created: list[ApprovalItem] = []
    key = _session_key(run.project_id, run.session_id)

    for violation in run.violations:
        if violation.severity != "high":
            continue

        approval_id = f"approval_{run.run_id}_{violation.id}"
        with store_lock:
            if approval_id in approvals:
                continue

            item = ApprovalItem(
                approval_id=approval_id,
                session_id=run.session_id,
                project_id=run.project_id,
                item_type="write-action",
                ref_id=violation.id,
                title=f"Approve fix for {violation.category}",
                reason="High severity violation requires explicit approval before write actions.",
                status="pending",
                requested_at=_utc_now(),
            )
            approvals[approval_id] = item
            _append_unique(approval_index_by_session, key, approval_id)
            created.append(item)

    return created


def _build_validation_response(run: ValidationRunRecord) -> ValidationResponse:
    return ValidationResponse(
        run_id=run.run_id,
        status=run.status,
        categories=run.categories,
        violations=run.violations,
        rule_pack_version=run.rule_pack_version,
        worker_build_hash=run.worker_build_hash,
        constraint_profile_name=run.constraint_profile_name,
        applied_constraints=run.applied_constraints,
        severity_counts=run.severity_counts,
        degraded_reason_codes=run.degraded_reason_codes,
    )


def _build_guardrail_status(project_id: str, session_id: str) -> GuardrailStatusDTO:
    session_key = (project_id, session_id)
    with store_lock:
        run_id = latest_run_by_session.get(session_key)
        run = validation_runs.get(run_id) if run_id else None

    if not run:
        unknown_categories = [
            ValidationCategory(name=name, status="unknown", severity_counts=_empty_severity_counts())
            for name in sorted(rule_engine.get_categories())
        ]

        return GuardrailStatusDTO(
            project_id=project_id,
            session_id=session_id,
            run_id=None,
            run_status="idle",
            category_state=unknown_categories,
            severity_counts=_empty_severity_counts(),
            degraded_reason_codes=[],
            constraint_profile_name=None,
            applied_constraints=None,
            updated_at=_utc_now(),
        )

    return GuardrailStatusDTO(
        project_id=run.project_id,
        session_id=run.session_id,
        run_id=run.run_id,
        run_status=run.status,
        category_state=run.categories,
        severity_counts=run.severity_counts,
        degraded_reason_codes=run.degraded_reason_codes,
        constraint_profile_name=run.constraint_profile_name,
        applied_constraints=run.applied_constraints,
        updated_at=run.ended_at or run.started_at,
    )


def _execute_validation(request: ValidationRequest) -> ValidationRunRecord:
    started_at = _utc_now()
    session_key = (request.project_id, request.session_id)
    constraint_profile = _resolve_constraint_profile(request.project_id, request.drawing_id)

    run_record = ValidationRunRecord(
        run_id=request.run_id,
        session_id=request.session_id,
        project_id=request.project_id,
        drawing_id=request.drawing_id,
        geometry_version_id=request.geometry_version_id,
        rule_pack_version=request.rule_pack_version,
        worker_build_hash=WORKER_BUILD_HASH,
        constraint_profile_name=constraint_profile.profile_name,
        applied_constraints=constraint_profile.constraints,
        correlation_id=request.correlation_id,
        status="running",
        requested_at=request.requested_at,
        started_at=started_at,
        categories=[],
        violations=[],
        severity_counts=_empty_severity_counts(),
        degraded_reason_codes=[],
    )

    with store_lock:
        prior_run_id = active_run_by_session.get(session_key)
        if prior_run_id and prior_run_id in validation_runs:
            prior_run = validation_runs[prior_run_id]
            if prior_run.status == "running":
                prior_run.status = "superseded"
                prior_run.ended_at = started_at

        validation_runs[request.run_id] = run_record
        latest_run_by_session[session_key] = request.run_id
        active_run_by_session[session_key] = request.run_id

    run_status = "completed"
    degraded_reason_codes: list[str] = []

    try:
        violations = rule_engine.validate(request.entities, constraint_profile.constraints)
        categories = _compute_category_state(violations)
    except Exception:
        violations = []
        categories = [
            ValidationCategory(name=name, status="unknown", severity_counts=_empty_severity_counts())
            for name in sorted(rule_engine.get_categories())
        ]
        run_status = "degraded"
        degraded_reason_codes = ["rule_engine_error"]

    severity_counts = _aggregate_severity_counts(categories)
    ended_at = _utc_now()

    with store_lock:
        persisted = validation_runs[request.run_id]
        persisted.categories = categories
        persisted.violations = violations
        persisted.severity_counts = severity_counts
        persisted.degraded_reason_codes = degraded_reason_codes
        persisted.ended_at = ended_at

        # Preserve superseded status if a newer run started while this run was evaluating.
        if persisted.status != "superseded":
            persisted.status = run_status

        if active_run_by_session.get(session_key) == request.run_id:
            active_run_by_session.pop(session_key, None)

    _upsert_report_for_run(persisted)
    for violation in persisted.violations:
        if violation.severity in {"high", "medium"}:
            _create_issue_from_violation(persisted, violation)
    _sync_approvals_for_run(persisted)

    return persisted


def _from_legacy_payload(payload: GeometryPayload) -> ValidationRequest:
    return ValidationRequest(
        run_id=f"run_{uuid.uuid4().hex[:10]}",
        session_id=payload.session_id,
        project_id="default-project",
        drawing_id="active-drawing",
        geometry_version_id=payload.geometry_version,
        rule_pack_version="rp-default",
        correlation_id=f"corr_{uuid.uuid4().hex[:12]}",
        requested_at=_utc_now(),
        entities=payload.entities,
    )


def _maybe_generate_groq_response(
    request: AgentChatRequest,
    summary: str,
    suggested_tools: list[str],
    fallback_answer: str,
) -> str | None:
    if not GROQ_API_KEY:
        return None

    system_prompt = (
        "You are CADY, an AutoCAD compliance workspace assistant. "
        "Deterministic validation outputs are the source of truth. "
        "Do not fabricate run data. Keep responses short and practical."
    )

    user_prompt = (
        f"Mode: {request.mode}\n"
        f"User message: {request.message}\n"
        f"Current deterministic summary: {summary}\n"
        f"Suggested tools: {', '.join(suggested_tools)}\n"
        f"Fallback answer: {fallback_answer}\n"
        "Respond with concise guidance in plain text."
    )

    payload = {
        "model": GROQ_MODEL,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_prompt},
        ],
        "temperature": 0.3,
        "max_tokens": 350,
        "top_p": 0.95,
        "stream": False,
    }

    groq_request = urllib.request.Request(
        "https://api.groq.com/openai/v1/chat/completions",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {GROQ_API_KEY}",
            "Content-Type": "application/json",
        },
        method="POST",
    )

    try:
        with urllib.request.urlopen(groq_request, timeout=25) as response:
            body = response.read().decode("utf-8")
            parsed = json.loads(body)
            content = parsed["choices"][0]["message"]["content"].strip()
            return content or None
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, KeyError, json.JSONDecodeError) as ex:
        print(f"Groq call failed. Using deterministic fallback. Error: {ex}")
        return None


@app.get("/health")
async def health_check():
    return {
        "status": "healthy",
        "service": "autocad-validation-agent",
        "timestamp": _utc_now().isoformat(),
        "version": app.version,
        "groq_enabled": bool(GROQ_API_KEY),
    }


@app.get("/rules", response_model=RuleListResponse)
async def list_rules():
    return rule_engine.get_rules_summary()


@app.get("/tools/get_project_constraints", response_model=ProjectConstraintsResponse)
async def get_project_constraints(project_id: str, drawing_id: str = "active-drawing"):
    return _resolve_constraint_profile(project_id, drawing_id)


@app.post("/tools/configure_project_constraints", response_model=ProjectConstraintsResponse)
async def configure_project_constraints(request: ProjectConstraintsConfigRequest):
    return _upsert_constraint_profile(request)


@app.post("/validate", response_model=ValidationResponse)
async def validate_geometry(payload: GeometryPayload):
    request = _from_legacy_payload(payload)
    run = _execute_validation(request)
    return _build_validation_response(run)


@app.post("/tools/run_validation", response_model=ValidationResponse)
async def run_validation_tool(request: ValidationRequest):
    run = _execute_validation(request)
    return _build_validation_response(run)


@app.get("/projects/{project_id}/sessions/{session_id}/guardrail-status", response_model=GuardrailStatusDTO)
async def get_guardrail_status(project_id: str, session_id: str):
    return _build_guardrail_status(project_id, session_id)


@app.get("/tools/get_guardrail_status", response_model=GuardrailStatusDTO)
async def get_guardrail_status_tool(project_id: str, session_id: str):
    return _build_guardrail_status(project_id, session_id)


@app.get("/validation-runs/{run_id}", response_model=ValidationRunRecord)
async def get_validation_run(run_id: str):
    with store_lock:
        run = validation_runs.get(run_id)

    if not run:
        raise HTTPException(status_code=404, detail=f"Validation run '{run_id}' was not found")

    return run


@app.get("/tools/get_validation_run/{run_id}", response_model=ValidationRunRecord)
async def get_validation_run_tool(run_id: str):
    return await get_validation_run(run_id)


@app.post("/tools/subscribe_guardrail_updates", response_model=GuardrailSubscriptionResponse)
async def subscribe_guardrail_updates(request: GuardrailSubscriptionRequest):
    mode = "poll" if request.preferred_mode != "websocket" else "poll"
    return GuardrailSubscriptionResponse(
        session_id=request.session_id,
        project_id=request.project_id,
        mode=mode,
        poll_interval_seconds=DEFAULT_POLL_INTERVAL_SECONDS,
        channel=f"guardrail.{request.session_id}",
    )


@app.post("/tools/get_active_document_context", response_model=ActiveDocumentContextResponse)
async def get_active_document_context(request: ActiveDocumentContextRequest):
    return ActiveDocumentContextResponse(
        session_id=request.session_id,
        project_id=request.project_id,
        drawing_id=request.drawing_id,
        geometry_version_id=request.geometry_version_id,
        entity_count=request.entity_count,
        layer_count=request.layer_count,
        units=request.units,
        captured_at=_utc_now(),
    )


@app.get("/tools/get_violation_context/{run_id}", response_model=list[Violation])
async def get_violation_context(run_id: str):
    run = await get_validation_run(run_id)
    return run.violations


@app.get("/tools/generate_fix_recommendations/{run_id}", response_model=RecommendationBundle)
async def generate_fix_recommendations(run_id: str):
    run = await get_validation_run(run_id)
    return RecommendationBundle(run_id=run_id, options=_generate_recommendations_for_run(run))


@app.post("/tools/create_issue", response_model=IssueRecord)
async def create_issue(request: CreateIssueRequest):
    with store_lock:
        run = validation_runs.get(request.run_id)

    if not run:
        raise HTTPException(status_code=404, detail=f"Validation run '{request.run_id}' was not found")

    violation = next((item for item in run.violations if item.id == request.violation_id), None)
    if not violation:
        raise HTTPException(status_code=404, detail=f"Violation '{request.violation_id}' was not found")

    issue = _create_issue_from_violation(run, violation, request.assignee)

    with store_lock:
        issue.title = request.title
        issue.severity = request.severity
        issue.updated_at = _utc_now()
        issues[issue.issue_id] = issue

    return issue


@app.get("/tools/list_issues", response_model=list[IssueRecord])
async def list_issues(project_id: str, session_id: str):
    rows = _list_session_issues(project_id, session_id)
    return sorted(rows, key=lambda item: item.updated_at, reverse=True)


@app.post("/tools/transition_issue_state", response_model=IssueRecord)
async def transition_issue_state(request: TransitionIssueStateRequest):
    with store_lock:
        issue = issues.get(request.issue_id)
        if not issue:
            raise HTTPException(status_code=404, detail=f"Issue '{request.issue_id}' was not found")

        issue.status = request.new_status
        issue.updated_at = _utc_now()
        issues[issue.issue_id] = issue

    return issue


@app.post("/tools/generate_validation_report", response_model=ReportRecord)
async def generate_validation_report(request: GenerateReportRequest):
    run = await get_validation_run(request.run_id)
    return _upsert_report_for_run(run)


@app.get("/tools/get_report_status", response_model=list[ReportRecord])
async def get_report_status(project_id: str, session_id: str):
    rows = _list_session_reports(project_id, session_id)
    return sorted(rows, key=lambda item: item.created_at, reverse=True)


@app.get("/tools/list_approvals", response_model=list[ApprovalItem])
async def list_approvals(project_id: str, session_id: str):
    rows = _list_session_approvals(project_id, session_id)
    return sorted(rows, key=lambda item: item.requested_at, reverse=True)


@app.post("/tools/approve_item", response_model=ApprovalItem)
async def approve_item(request: ApprovalDecisionRequest):
    with store_lock:
        item = approvals.get(request.approval_id)
        if not item:
            raise HTTPException(status_code=404, detail=f"Approval item '{request.approval_id}' was not found")

        if request.decision not in {"approved", "rejected"}:
            raise HTTPException(status_code=400, detail="Decision must be 'approved' or 'rejected'")

        item.status = request.decision
        approvals[item.approval_id] = item

    return item


@app.post("/agent/chat", response_model=AgentChatResponse)
async def agent_chat(request: AgentChatRequest):
    lowered_message = request.message.lower()
    guardrail = _build_guardrail_status(request.project_id, request.session_id)

    summary = (
        f"Latest run status: {guardrail.run_status}. "
        f"High: {guardrail.severity_counts.get('high', 0)}, "
        f"Medium: {guardrail.severity_counts.get('medium', 0)}, "
        f"Low: {guardrail.severity_counts.get('low', 0)}. "
        f"Constraint profile: {guardrail.constraint_profile_name or 'not-configured'}"
    )

    if any(token in lowered_message for token in ["validate", "check", "re-run", "rerun"]):
        answer = (
            "Use deterministic validation tools for this request. "
            "Trigger geometry extraction, run validation, then refresh guardrail status. "
            + summary
        )
        suggested_tools = [
            "extract_geometry_snapshot",
            "run_validation",
            "get_guardrail_status",
        ]
    elif "run" in lowered_message and "detail" in lowered_message:
        run_label = guardrail.run_id or "latest run"
        answer = f"Fetch run details using get_validation_run for {run_label}. {summary}"
        suggested_tools = ["get_validation_run"]
    else:
        answer = (
            "Deterministic validation remains the source of truth. "
            f"{summary} I can help prioritize categories and suggest next tool calls."
        )
        suggested_tools = ["get_guardrail_status"]

    if request.mode.lower().startswith("agent"):
        answer += " Agent mode can recommend write tools, but execution still requires explicit approval."

    groq_answer = _maybe_generate_groq_response(request, summary, suggested_tools, answer)
    if groq_answer:
        answer = groq_answer

    return AgentChatResponse(answer=answer, suggested_tools=suggested_tools)


@app.post("/agent/unified-query", response_model=UnifiedAgentResponse)
async def unified_agent_query(request: UnifiedAgentQueryRequest):
    guardrail = _build_guardrail_status(request.project_id, request.session_id)
    constraints = _resolve_constraint_profile(request.project_id, request.drawing_id)
    executed_tools = [
        "get_active_document_context",
        "get_project_constraints",
        "get_guardrail_status",
    ]

    latest_run = None
    recommendations: list[RecommendationOption] = []

    if guardrail.run_id:
        executed_tools.extend([
            "get_validation_run",
            "get_violation_context",
            "generate_fix_recommendations",
        ])
        try:
            latest_run = await get_validation_run(guardrail.run_id)
        except HTTPException:
            latest_run = None

    if latest_run:
        recommendations = _generate_recommendations_for_run(latest_run)
        _upsert_report_for_run(latest_run)
        _sync_approvals_for_run(latest_run)
        for violation in latest_run.violations:
            if violation.severity in {"high", "medium"}:
                _create_issue_from_violation(latest_run, violation)

        executed_tools.extend([
            "create_issue",
            "generate_validation_report",
            "list_issues",
            "get_report_status",
            "list_approvals",
        ])

    issue_rows = _list_session_issues(request.project_id, request.session_id)
    report_rows = _list_session_reports(request.project_id, request.session_id)
    approval_rows = _list_session_approvals(request.project_id, request.session_id)

    summary = (
        f"Run status: {guardrail.run_status}. "
        f"High: {guardrail.severity_counts.get('high', 0)}, "
        f"Medium: {guardrail.severity_counts.get('medium', 0)}, "
        f"Low: {guardrail.severity_counts.get('low', 0)}. "
        f"Constraint profile: {constraints.profile_name} ({constraints.source})."
    )

    answer = (
        "Unified agent refreshed all tool outputs. "
        f"{summary} "
        f"Recommendations: {len(recommendations)}. "
        f"Issues: {len(issue_rows)}. "
        f"Reports: {len(report_rows)}. "
        f"Approvals: {len(approval_rows)}."
    )

    if request.message.strip():
        answer += f" Request handled: {request.message.strip()}"

    groq_chat_request = AgentChatRequest(
        session_id=request.session_id,
        project_id=request.project_id,
        drawing_id=request.drawing_id,
        mode="Unified Agent",
        message=request.message,
    )
    unique_tools = list(dict.fromkeys(executed_tools))
    groq_answer = _maybe_generate_groq_response(groq_chat_request, summary, unique_tools, answer)
    if groq_answer:
        answer = groq_answer

    return UnifiedAgentResponse(
        answer=answer,
        executed_tools=unique_tools,
        guardrail_status=guardrail,
        latest_run=latest_run,
        recommendations=recommendations,
        issues=sorted(issue_rows, key=lambda item: item.updated_at, reverse=True),
        reports=sorted(report_rows, key=lambda item: item.created_at, reverse=True),
        approvals=sorted(approval_rows, key=lambda item: item.requested_at, reverse=True),
    )


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="127.0.0.1", port=8000)
