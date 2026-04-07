"""Data contracts for deterministic validation and tool-oriented agent orchestration."""

from datetime import datetime
from pydantic import BaseModel, Field
from typing import Optional


class EntityProperties(BaseModel):
    """Properties of a CAD entity - varies by type."""

    # Circle properties
    radius: Optional[float] = None
    center: Optional[list[float]] = None

    # Line properties
    start_point: Optional[list[float]] = None
    end_point: Optional[list[float]] = None
    length: Optional[float] = None

    # Arc properties
    start_angle: Optional[float] = None
    end_angle: Optional[float] = None

    # Text properties
    text_height: Optional[float] = None
    text_content: Optional[str] = None

    # Common properties
    area: Optional[float] = None
    color: Optional[int] = None


class Entity(BaseModel):
    """A single CAD entity."""

    handle: str
    type: str  # Circle, Line, Arc, Text, Polyline, etc.
    layer: str
    properties: EntityProperties


class GeometryPayload(BaseModel):
    """Legacy payload sent from AutoCAD plugin for validation."""

    session_id: str
    geometry_version: str = "v1"
    entities: list[Entity]


class ValidationConstraints(BaseModel):
    """Project-specific validation thresholds and rule toggles."""

    min_circle_radius: float = 5.0
    max_circle_radius: float = 500.0
    max_line_length: float = 1000.0
    min_text_height: float = 2.5
    min_arc_angle_degrees: float = 5.0
    disallowed_layers: list[str] = Field(default_factory=lambda: ["0"])
    notes: Optional[str] = None


class ProjectConstraintsConfigRequest(BaseModel):
    """Request to configure constraints for a project + drawing scope."""

    project_id: str = "default-project"
    drawing_id: str = "active-drawing"
    profile_name: str = "default-profile"
    constraints: ValidationConstraints


class ProjectConstraintsResponse(BaseModel):
    """Resolved constraint profile for a project + drawing scope."""

    project_id: str
    drawing_id: str
    profile_name: str
    source: str  # default or custom
    constraints: ValidationConstraints
    configured_at: datetime


class ValidationRequest(BaseModel):
    """Canonical request contract for `run_validation`."""

    run_id: str
    session_id: str
    project_id: str = "default-project"
    drawing_id: str = "active-drawing"
    geometry_version_id: str
    rule_pack_version: str = "rp-default"
    correlation_id: str
    requested_at: datetime
    entities: list[Entity]


class Violation(BaseModel):
    """A single rule violation."""

    id: str
    rule_id: str
    category: str
    severity: str  # high, medium, low
    message: str
    entity_ref: str  # Handle of the violating entity


class ValidationCategory(BaseModel):
    """Validation status for a category."""

    name: str
    status: str  # pass, fail, unknown
    severity_counts: dict[str, int]


class GuardrailStatusDTO(BaseModel):
    """Feature 01 panel status contract for latest session run."""

    project_id: str
    session_id: str
    run_id: Optional[str] = None
    run_status: str
    category_state: list[ValidationCategory]
    severity_counts: dict[str, int]
    degraded_reason_codes: list[str]
    constraint_profile_name: Optional[str] = None
    applied_constraints: Optional[ValidationConstraints] = None
    updated_at: datetime


class ValidationResponse(BaseModel):
    """Response returned by validation endpoints."""

    run_id: str
    status: str  # completed, degraded, superseded
    categories: list[ValidationCategory]
    violations: list[Violation]
    rule_pack_version: str
    worker_build_hash: str
    constraint_profile_name: str
    applied_constraints: ValidationConstraints
    severity_counts: dict[str, int]
    degraded_reason_codes: list[str] = Field(default_factory=list)


class ValidationRunRecord(BaseModel):
    """Persisted run state used by `get_validation_run` and status queries."""

    run_id: str
    session_id: str
    project_id: str
    drawing_id: str
    geometry_version_id: str
    rule_pack_version: str
    worker_build_hash: str
    constraint_profile_name: str
    applied_constraints: ValidationConstraints
    correlation_id: str
    status: str  # running, completed, degraded, superseded
    requested_at: datetime
    started_at: datetime
    ended_at: Optional[datetime] = None
    categories: list[ValidationCategory]
    violations: list[Violation]
    severity_counts: dict[str, int]
    degraded_reason_codes: list[str] = Field(default_factory=list)


class GuardrailSubscriptionRequest(BaseModel):
    """Request contract for `subscribe_guardrail_updates`."""

    session_id: str
    project_id: str = "default-project"
    preferred_mode: str = "poll"


class GuardrailSubscriptionResponse(BaseModel):
    """Subscription acknowledgment contract."""

    session_id: str
    project_id: str
    mode: str
    poll_interval_seconds: int
    channel: str


class AgentChatRequest(BaseModel):
    """Chat request sent by the plugin workspace UI."""

    session_id: str
    project_id: str = "default-project"
    drawing_id: str = "active-drawing"
    mode: str = "Design Validation Mode"
    message: str


class AgentChatResponse(BaseModel):
    """Deterministic agent response with tool guidance."""

    answer: str
    suggested_tools: list[str]


class ActiveDocumentContextRequest(BaseModel):
    """Local AutoCAD context forwarded to backend for orchestration traceability."""

    session_id: str
    project_id: str = "default-project"
    drawing_id: str = "active-drawing"
    geometry_version_id: str = "v1"
    entity_count: int = 0
    layer_count: int = 0
    units: str = "mm"


class ActiveDocumentContextResponse(BaseModel):
    """Echoed context snapshot with backend timestamp."""

    session_id: str
    project_id: str
    drawing_id: str
    geometry_version_id: str
    entity_count: int
    layer_count: int
    units: str
    captured_at: datetime


class RecommendationOption(BaseModel):
    """A recommendation card generated for a violation."""

    option_id: str
    title: str
    rationale: str
    target_entity_ref: Optional[str] = None
    confidence: float
    requires_approval: bool = False


class RecommendationBundle(BaseModel):
    """Recommendation set generated for a validation run."""

    run_id: str
    options: list[RecommendationOption]


class CreateIssueRequest(BaseModel):
    """Request to create an issue from a violation."""

    session_id: str
    project_id: str = "default-project"
    run_id: str
    violation_id: str
    title: str
    severity: str
    assignee: Optional[str] = None


class TransitionIssueStateRequest(BaseModel):
    """Request to transition an issue state."""

    issue_id: str
    new_status: str


class IssueRecord(BaseModel):
    """Tracked issue derived from violations."""

    issue_id: str
    session_id: str
    project_id: str
    run_id: str
    violation_id: str
    title: str
    severity: str
    status: str
    assignee: Optional[str] = None
    created_at: datetime
    updated_at: datetime


class GenerateReportRequest(BaseModel):
    """Request to generate a validation report."""

    session_id: str
    project_id: str = "default-project"
    run_id: str


class ReportRecord(BaseModel):
    """Report summary generated from a validation run."""

    report_id: str
    session_id: str
    project_id: str
    run_id: str
    status: str
    compliance_score: float
    summary: str
    created_at: datetime


class ApprovalItem(BaseModel):
    """Approval queue item for high-impact proposed actions."""

    approval_id: str
    session_id: str
    project_id: str
    item_type: str
    ref_id: str
    title: str
    reason: str
    status: str
    requested_at: datetime


class ApprovalDecisionRequest(BaseModel):
    """Request to approve or reject an approval queue item."""

    approval_id: str
    decision: str


class UnifiedAgentQueryRequest(BaseModel):
    """Single-agent query request for unified workspace outputs."""

    session_id: str
    project_id: str = "default-project"
    drawing_id: str = "active-drawing"
    message: str


class UnifiedAgentResponse(BaseModel):
    """Single-agent response with all unified workspace sections."""

    answer: str
    executed_tools: list[str]
    guardrail_status: GuardrailStatusDTO
    latest_run: Optional[ValidationRunRecord] = None
    recommendations: list[RecommendationOption] = Field(default_factory=list)
    issues: list[IssueRecord] = Field(default_factory=list)
    reports: list[ReportRecord] = Field(default_factory=list)
    approvals: list[ApprovalItem] = Field(default_factory=list)


class RuleDefinition(BaseModel):
    """Definition of a single validation rule."""

    id: str
    name: str
    category: str
    severity: str
    description: str
    entity_types: list[str]  # Which entity types this rule applies to
    condition: dict  # Rule condition (property, operator, value)


class RuleListResponse(BaseModel):
    """Response from rules listing endpoint."""

    categories: list[str]
    rules: list[RuleDefinition]
    total_count: int
