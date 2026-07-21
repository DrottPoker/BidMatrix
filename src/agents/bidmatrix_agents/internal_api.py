import json
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any
from uuid import uuid4

import httpx
from pydantic import BaseModel, ConfigDict, Field

from bidmatrix_agents.settings import WorkerSettings
from bidmatrix_agents.workflows.models import (
    AgentExecution,
    AgentFailureInput,
    AgentTaskWorkflowInput,
    AgentTaskWorkflowResult,
    AnalysisIntakeInput,
    AnalysisIntakeState,
    ApprovalDecision,
    ApprovalWorkflowInput,
    PreparedAgentTask,
)


class ClaimedEvent(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    event_id: str = Field(alias="eventId")
    event_type: str = Field(alias="eventType")
    aggregate_id: str = Field(alias="aggregateId")
    payload: str
    attempt_count: int = Field(alias="attemptCount")


class ClaimEventsResponse(BaseModel):
    events: list[ClaimedEvent]


class AnalysisIntakeStateResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    analysis_id: str = Field(alias="analysisId")
    organization_id: str = Field(alias="organizationId")
    status: str
    file_scan_statuses: list[str] = Field(alias="fileScanStatuses")


class ManualReviewTaskResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    task_id: str = Field(alias="taskId")


class ApprovalStateResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    status: str
    execution_status: str | None = Field(alias="executionStatus")


class PreparedAgentTaskResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    task_id: str = Field(alias="taskId")
    organization_id: str = Field(alias="organizationId")
    agent_run_id: str = Field(alias="agentRunId")
    agent_key: str = Field(alias="agentKey")
    agent_version: int = Field(alias="agentVersion")
    model_name: str = Field(alias="modelName")
    prompt_version: str = Field(alias="promptVersion")
    allowed_tools: list[str] = Field(alias="allowedTools")
    input_data: dict[str, Any] = Field(alias="input")
    workflow_id: str = Field(alias="workflowId")
    correlation_id: str = Field(alias="correlationId")


class ToolGatewayCallResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    decision: str
    tool_call_id: str = Field(alias="toolCallId")
    approval_id: str | None = Field(alias="approvalId")
    execution_status: str = Field(alias="executionStatus")
    output: dict[str, Any] | None


class AgentRunStateResponse(BaseModel):
    status: str


@dataclass(frozen=True)
class InternalApiError(Exception):
    operation: str
    status_code: int

    def __str__(self) -> str:
        return f"{self.operation} failed with HTTP {self.status_code}"


class InternalApiClient:
    def __init__(self, settings: WorkerSettings) -> None:
        self._client = httpx.AsyncClient(
            base_url=settings.internal_base_url,
            headers={
                "Authorization": f"Bearer {settings.internal_service_token.get_secret_value()}",
                "Accept": "application/json",
            },
            timeout=httpx.Timeout(10.0),
        )

    async def __aenter__(self) -> InternalApiClient:
        return self

    async def __aexit__(self, *_: object) -> None:
        await self._client.aclose()

    async def claim_events(self, event_type: str, limit: int = 10) -> list[ClaimedEvent]:
        response = await self._client.get(
            "/internal/v1/events/claim",
            params={"eventType": event_type, "limit": limit},
        )
        self._ensure_success(f"claim {event_type} events", response)
        return ClaimEventsResponse.model_validate(response.json()).events

    async def acknowledge_event(self, event_id: str) -> None:
        response = await self._client.post(f"/internal/v1/events/{event_id}/ack")
        self._ensure_success("acknowledge event", response)

    async def fail_event(self, event_id: str, error: str) -> None:
        response = await self._client.post(
            f"/internal/v1/events/{event_id}/fail",
            json={"error": error[:2_000]},
        )
        self._ensure_success("fail event", response)

    async def load_analysis_intake(self, request: AnalysisIntakeInput) -> AnalysisIntakeState:
        response = await self._client.get(
            f"/internal/v1/analyses/{request.analysis_id}/intake",
            params={"organizationId": request.organization_id},
        )
        self._ensure_success("load analysis intake", response)
        state = AnalysisIntakeStateResponse.model_validate(response.json())
        return AnalysisIntakeState(
            analysis_id=state.analysis_id,
            organization_id=state.organization_id,
            status=state.status,
            file_scan_statuses=state.file_scan_statuses,
        )

    async def mark_processing(self, request: AnalysisIntakeInput) -> None:
        await self._post_intake(request, "processing")

    async def create_manual_review_task(self, request: AnalysisIntakeInput) -> str:
        response = await self._post_intake(request, "manual-review-task")
        return ManualReviewTaskResponse.model_validate(response.json()).task_id

    async def mark_requires_review(self, request: AnalysisIntakeInput) -> None:
        await self._post_intake(request, "requires-review")

    async def expire_approval(self, approval_id: str) -> ApprovalDecision:
        response = await self._client.post(f"/internal/v1/approvals/{approval_id}/expire")
        self._ensure_success("expire approval", response)
        approval = ApprovalStateResponse.model_validate(response.json())
        return ApprovalDecision(
            status=approval.status,
            execution_status=approval.execution_status or "notStarted",
        )

    async def prepare_agent_task(
        self,
        request: AgentTaskWorkflowInput,
        runtime_mode: str,
        model_name: str,
    ) -> PreparedAgentTask:
        response = await self._client.post(
            f"/internal/v1/agent-tasks/{request.task_id}/prepare",
            json={
                "organizationId": request.organization_id,
                "workflowId": request.workflow_id,
                "correlationId": request.correlation_id,
                "runtimeMode": runtime_mode,
                "modelName": model_name,
            },
        )
        self._ensure_success("prepare agent task", response)
        prepared = PreparedAgentTaskResponse.model_validate(response.json())
        return PreparedAgentTask(
            task_id=prepared.task_id,
            organization_id=prepared.organization_id,
            agent_run_id=prepared.agent_run_id,
            agent_key=prepared.agent_key,
            agent_version=prepared.agent_version,
            model_name=prepared.model_name,
            prompt_version=prepared.prompt_version,
            allowed_tools=prepared.allowed_tools,
            input_data=prepared.input_data,
            workflow_id=prepared.workflow_id,
            correlation_id=prepared.correlation_id,
        )

    async def execute_tool(
        self,
        preparation: PreparedAgentTask,
        tool_key: str,
        idempotency_key: str,
        arguments: dict[str, Any],
    ) -> ToolGatewayCallResponse:
        response = await self._client.post(
            "/internal/v1/tool-gateway/calls",
            json={
                "requestId": str(uuid4()),
                "taskId": preparation.task_id,
                "agentRunId": preparation.agent_run_id,
                "agentKey": preparation.agent_key,
                "toolKey": tool_key,
                "idempotencyKey": idempotency_key,
                "arguments": arguments,
                "context": {
                    "organizationId": preparation.organization_id,
                    "correlationId": preparation.correlation_id,
                },
            },
        )
        self._ensure_success(f"execute {tool_key}", response)
        return ToolGatewayCallResponse.model_validate(response.json())

    async def complete_agent_task(
        self,
        preparation: PreparedAgentTask,
        execution: AgentExecution,
        output_artifact_id: str,
    ) -> AgentTaskWorkflowResult:
        response = await self._client.post(
            f"/internal/v1/agent-tasks/{preparation.task_id}/complete",
            json={
                "organizationId": preparation.organization_id,
                "agentRunId": preparation.agent_run_id,
                "outputArtifactId": output_artifact_id,
                "output": execution.output,
                "requestCount": execution.usage.request_count,
                "inputTokens": execution.usage.input_tokens,
                "outputTokens": execution.usage.output_tokens,
                "reasoningTokens": execution.usage.reasoning_tokens,
                "correlationId": preparation.correlation_id,
            },
        )
        self._ensure_success("complete agent task", response)
        run = AgentRunStateResponse.model_validate(response.json())
        return AgentTaskWorkflowResult(
            task_id=preparation.task_id,
            agent_run_id=preparation.agent_run_id,
            output_artifact_id=output_artifact_id,
            status=run.status,
        )

    async def fail_agent_task(self, failure: AgentFailureInput) -> None:
        request = failure.workflow_input
        response = await self._client.post(
            f"/internal/v1/agent-tasks/{request.task_id}/fail",
            json={
                "organizationId": request.organization_id,
                "agentRunId": failure.agent_run_id,
                "failureCode": failure.failure_code,
                "failureMessage": failure.failure_message,
                "correlationId": request.correlation_id,
            },
        )
        self._ensure_success("fail agent task", response)

    async def _post_intake(self, request: AnalysisIntakeInput, operation: str) -> httpx.Response:
        response = await self._client.post(
            f"/internal/v1/analyses/{request.analysis_id}/intake/{operation}",
            json={
                "organizationId": request.organization_id,
                "correlationId": request.correlation_id,
            },
        )
        self._ensure_success(operation, response)
        return response

    @staticmethod
    def _ensure_success(operation: str, response: httpx.Response) -> None:
        if not response.is_success:
            raise InternalApiError(operation, response.status_code)


def parse_analysis_submitted_event(event: ClaimedEvent) -> tuple[str, AnalysisIntakeInput]:
    envelope, payload = _parse_event_envelope(event)

    analysis_id = payload.get("analysisId")
    workflow_id = payload.get("workflowId")
    organization_id = envelope.get("organizationId")
    correlation_id = envelope.get("correlationId")
    if not isinstance(analysis_id, str) or not analysis_id:
        raise ValueError("Outbox event is missing analysisId.")
    if not isinstance(workflow_id, str) or not workflow_id:
        raise ValueError("Outbox event is missing workflowId.")
    if not isinstance(organization_id, str) or not organization_id:
        raise ValueError("Outbox event is missing organizationId.")
    if not isinstance(correlation_id, str) or not correlation_id:
        raise ValueError("Outbox event is missing required analysis workflow fields.")

    return workflow_id, AnalysisIntakeInput(
        analysis_id=analysis_id,
        organization_id=organization_id,
        correlation_id=correlation_id,
    )


def parse_approval_requested_event(event: ClaimedEvent) -> tuple[str, ApprovalWorkflowInput]:
    envelope, payload = _parse_event_envelope(event)
    approval_id = _require_event_string(payload.get("approvalId"), "approvalId")
    workflow_id = _require_event_string(payload.get("workflowId"), "workflowId")
    expires_at = _require_event_string(payload.get("expiresAt"), "expiresAt")
    organization_id = _require_event_string(envelope.get("organizationId"), "organizationId")
    correlation_id = _require_event_string(envelope.get("correlationId"), "correlationId")

    return workflow_id, ApprovalWorkflowInput(
        approval_id=approval_id,
        organization_id=organization_id,
        correlation_id=correlation_id,
        expires_at=expires_at,
    )


def parse_approval_decided_event(event: ClaimedEvent) -> tuple[str, ApprovalDecision]:
    _, payload = _parse_event_envelope(event)
    workflow_id = _require_event_string(payload.get("workflowId"), "workflowId")
    status = _require_event_string(payload.get("status"), "status")
    execution_status = _require_event_string(payload.get("executionStatus"), "executionStatus")
    return workflow_id, ApprovalDecision(status=status, execution_status=execution_status)


def parse_agent_task_event(event: ClaimedEvent) -> tuple[str, AgentTaskWorkflowInput]:
    envelope, payload = _parse_event_envelope(event)
    task_id = _require_event_string(payload.get("taskId"), "taskId")
    agent_key = _require_event_string(payload.get("agentKey"), "agentKey")
    workflow_id = _require_event_string(payload.get("workflowId"), "workflowId")
    organization_id = _require_event_string(envelope.get("organizationId"), "organizationId")
    correlation_id = _require_event_string(envelope.get("correlationId"), "correlationId")
    return workflow_id, AgentTaskWorkflowInput(
        task_id=task_id,
        organization_id=organization_id,
        agent_key=agent_key,
        workflow_id=workflow_id,
        correlation_id=correlation_id,
    )


def _parse_event_envelope(event: ClaimedEvent) -> tuple[Mapping[str, Any], Mapping[str, Any]]:
    envelope: Mapping[str, Any] = json.loads(event.payload)
    payload = envelope.get("payload")
    if not isinstance(payload, Mapping):
        raise ValueError("Outbox event payload is missing the versioned payload object.")
    return envelope, payload


def _require_event_string(value: Any, field_name: str) -> str:
    if not isinstance(value, str) or not value:
        raise ValueError(f"Outbox event is missing {field_name}.")
    return value
