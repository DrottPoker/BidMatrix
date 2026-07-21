from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


class ProposedAction(StrictModel):
    tool_key: str
    arguments: dict[str, Any]
    rationale: str


class BaseAgentOutput(StrictModel):
    status: Literal["completed", "needs_attention"]
    summary: str
    findings: list[str]
    proposed_actions: list[ProposedAction]
    artifacts: list[str]
    uncertainties: list[str]
    requires_owner_attention: bool


class ExecutiveInput(StrictModel):
    goal_ids: list[str] = Field(alias="goalIds")
    task_summary: dict[str, Any] = Field(alias="taskSummary")
    metrics_snapshot: dict[str, Any] = Field(alias="metricsSnapshot")
    open_approvals: list[dict[str, Any]] = Field(alias="openApprovals")
    open_incidents: list[dict[str, Any]] = Field(alias="openIncidents")
    time_window: dict[str, str] = Field(alias="timeWindow")


class ExecutiveOutput(BaseAgentOutput):
    executive_summary: str
    metric_changes: list[str]
    risks: list[str]
    recommended_priorities: list[str]
    proposed_tasks: list[str]
    owner_decisions_needed: list[str]


class ConversationMessage(StrictModel):
    role: str
    body: str


class ApprovedKnowledge(StrictModel):
    source_id: str = Field(alias="sourceId")
    fact: str


class SupportInput(StrictModel):
    conversation: list[ConversationMessage]
    customer_context: dict[str, Any] = Field(alias="customerContext")
    approved_knowledge: list[ApprovedKnowledge] = Field(alias="approvedKnowledge")
    support_policy_version: str = Field(alias="supportPolicyVersion")
    sender_profile: str = Field(alias="senderProfile")


class MaterialClaim(StrictModel):
    claim: str
    source_ids: list[str]


class SupportOutput(BaseAgentOutput):
    classification: str
    urgency: Literal["low", "normal", "high", "critical"]
    draft_subject: str
    draft_body: str
    material_claims: list[MaterialClaim]
    sources: list[str]
    requires_escalation: bool
    escalation_reason: str | None


class MetricInput(StrictModel):
    key: str
    value: float
    sample_size: int = Field(alias="sampleSize", ge=0)


class AnalysisFailureInput(StrictModel):
    code: str
    count: int = Field(ge=0)


class ProductAnalystInput(StrictModel):
    metrics: list[MetricInput]
    support_themes: list[str] = Field(alias="supportThemes")
    analysis_failures: list[AnalysisFailureInput] = Field(alias="analysisFailures")
    owner_goals: list[str] = Field(alias="ownerGoals")
    period: dict[str, str]


class ExperimentProposal(StrictModel):
    problem: str
    evidence: list[str]
    hypothesis: str
    change: str
    primary_metric: str
    guardrail_metrics: list[str]
    sample_or_duration: str
    risk: str
    rollback_condition: str
    implementation_outline: list[str]


class ProductAnalystOutput(BaseAgentOutput):
    observations: list[str]
    hypotheses: list[str]
    recommended_experiments: list[ExperimentProposal]
    data_quality_issues: list[str]
    owner_decisions_needed: list[str]


class EngineeringInput(StrictModel):
    task_id: str = Field(alias="taskId")
    repository_path: str = Field(alias="repositoryPath")
    base_revision: str = Field(alias="baseRevision")
    requirements: list[str]
    allowed_commands: list[str] = Field(alias="allowedCommands")
    constraints: list[str]


class TestResult(StrictModel):
    command: str
    status: Literal["passed", "failed", "not_run"]
    summary: str


class PullRequestDraft(StrictModel):
    title: str
    body: str


class EngineeringOutput(BaseAgentOutput):
    implementation_summary: str
    files_changed: list[str]
    tests_run: list[str]
    test_results: list[TestResult]
    diff_artifact_id: str
    risks: list[str]
    follow_up_items: list[str]
    pull_request_draft: PullRequestDraft


type AgentInput = ExecutiveInput | SupportInput | ProductAnalystInput | EngineeringInput
type AgentOutput = ExecutiveOutput | SupportOutput | ProductAnalystOutput | EngineeringOutput


INPUT_MODELS: dict[str, type[AgentInput]] = {
    "executive": ExecutiveInput,
    "support": SupportInput,
    "product-analyst": ProductAnalystInput,
    "engineering": EngineeringInput,
}

OUTPUT_MODELS: dict[str, type[AgentOutput]] = {
    "executive": ExecutiveOutput,
    "support": SupportOutput,
    "product-analyst": ProductAnalystOutput,
    "engineering": EngineeringOutput,
}
