from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class AnalysisIntakeInput:
    analysis_id: str
    organization_id: str
    correlation_id: str


@dataclass(frozen=True)
class AnalysisIntakeState:
    analysis_id: str
    organization_id: str
    status: str
    file_scan_statuses: list[str]


@dataclass(frozen=True)
class AnalysisIntakeResult:
    analysis_id: str
    review_task_id: str
    status: str


@dataclass(frozen=True)
class ApprovalWorkflowInput:
    approval_id: str
    organization_id: str
    correlation_id: str
    expires_at: str


@dataclass(frozen=True)
class ApprovalDecision:
    status: str
    execution_status: str


@dataclass(frozen=True)
class ApprovalWorkflowResult:
    approval_id: str
    status: str
    execution_status: str


@dataclass(frozen=True)
class AgentTaskWorkflowInput:
    task_id: str
    organization_id: str
    agent_key: str
    workflow_id: str
    correlation_id: str


@dataclass(frozen=True)
class PreparedAgentTask:
    task_id: str
    organization_id: str
    agent_run_id: str
    agent_key: str
    agent_version: int
    model_name: str
    prompt_version: str
    allowed_tools: list[str]
    input_data: dict[str, Any]
    workflow_id: str
    correlation_id: str


@dataclass(frozen=True)
class AgentUsageRecord:
    request_count: int
    input_tokens: int
    output_tokens: int
    reasoning_tokens: int | None


@dataclass(frozen=True)
class AgentExecution:
    output: dict[str, Any]
    usage: AgentUsageRecord


@dataclass(frozen=True)
class AgentMaterializationInput:
    preparation: PreparedAgentTask
    execution: AgentExecution


@dataclass(frozen=True)
class AgentTaskWorkflowResult:
    task_id: str
    agent_run_id: str
    output_artifact_id: str
    status: str


@dataclass(frozen=True)
class AgentFailureInput:
    workflow_input: AgentTaskWorkflowInput
    agent_run_id: str | None
    failure_code: str
    failure_message: str
