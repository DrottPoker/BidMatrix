from typing import Any

from pydantic import ValidationError
from temporalio import activity
from temporalio.exceptions import ApplicationError

from bidmatrix_agents.agent_runtime.adapters import (
    run_configured_adapter,
    validate_tool_permissions,
)
from bidmatrix_agents.internal_api import InternalApiClient, InternalApiError
from bidmatrix_agents.settings import WorkerSettings
from bidmatrix_agents.workflows.models import (
    AgentExecution,
    AgentFailureInput,
    AgentMaterializationInput,
    AgentTaskWorkflowInput,
    AgentTaskWorkflowResult,
    AgentUsageRecord,
    PreparedAgentTask,
)


@activity.defn(name="prepare_agent_task")
async def prepare_agent_task(request: AgentTaskWorkflowInput) -> PreparedAgentTask:
    settings = WorkerSettings()
    try:
        async with InternalApiClient(settings) as client:
            return await client.prepare_agent_task(
                request,
                settings.agent_mode,
                settings.model_for_agent(request.agent_key),
            )
    except InternalApiError as error:
        raise _api_application_error(error) from error


@activity.defn(name="run_structured_agent")
async def run_structured_agent(preparation: PreparedAgentTask) -> AgentExecution:
    settings = WorkerSettings()
    try:
        result = await run_configured_adapter(
            settings,
            preparation.agent_key,
            preparation.input_data,
        )
        validate_tool_permissions(result.output, preparation.allowed_tools)
        output = result.output.model_dump(mode="json")
        return AgentExecution(
            output=output,
            usage=AgentUsageRecord(
                request_count=result.usage.request_count,
                input_tokens=result.usage.input_tokens,
                output_tokens=result.usage.output_tokens,
                reasoning_tokens=result.usage.reasoning_tokens,
            ),
        )
    except (ValidationError, ValueError, TimeoutError) as error:
        raise ApplicationError(
            "Agent output failed structured validation.",
            type=type(error).__name__,
            non_retryable=True,
        ) from error


@activity.defn(name="materialize_agent_output")
async def materialize_agent_output(
    request: AgentMaterializationInput,
) -> AgentTaskWorkflowResult:
    settings = WorkerSettings()
    preparation = request.preparation
    execution = request.execution
    try:
        async with InternalApiClient(settings) as client:
            artifact_call = await client.execute_tool(
                preparation,
                "artifact.createDraft",
                f"agent-output-{preparation.agent_run_id}",
                {
                    "title": f"{preparation.agent_key} F1 structured output",
                    "artifactType": f"{preparation.agent_key}_agent_output",
                    "content": execution.output,
                },
            )
            if artifact_call.decision not in {"allowed", "alreadyExecuted"}:
                raise ApplicationError(
                    "Output artifact was not allowed by the Tool Gateway.",
                    type="ToolGatewayDecision",
                    non_retryable=True,
                )
            artifact_id = _require_output_string(artifact_call.output, "artifactId")

            proposed_actions = execution.output.get("proposed_actions", [])
            if not isinstance(proposed_actions, list):
                raise ApplicationError(
                    "proposed_actions must be a list.",
                    type="StructuredOutputError",
                    non_retryable=True,
                )
            for index, action in enumerate(proposed_actions):
                if not isinstance(action, dict):
                    raise ApplicationError(
                        "Each proposed action must be an object.",
                        type="StructuredOutputError",
                        non_retryable=True,
                    )
                tool_key = _require_string(action, "tool_key")
                arguments = action.get("arguments")
                if not isinstance(arguments, dict):
                    raise ApplicationError(
                        "Proposed action arguments must be an object.",
                        type="StructuredOutputError",
                        non_retryable=True,
                    )
                decision = await client.execute_tool(
                    preparation,
                    tool_key,
                    f"agent-action-{preparation.agent_run_id}-{index}",
                    arguments,
                )
                if decision.decision in {"denied", "invalid"}:
                    raise ApplicationError(
                        f"Tool Gateway rejected proposed action {index}.",
                        type="ToolGatewayDecision",
                        non_retryable=True,
                    )

            return await client.complete_agent_task(preparation, execution, artifact_id)
    except InternalApiError as error:
        raise _api_application_error(error) from error


@activity.defn(name="fail_agent_task")
async def fail_agent_task(failure: AgentFailureInput) -> None:
    settings = WorkerSettings()
    try:
        async with InternalApiClient(settings) as client:
            await client.fail_agent_task(failure)
    except InternalApiError as error:
        raise _api_application_error(error) from error


def _api_application_error(error: InternalApiError) -> ApplicationError:
    non_retryable = 400 <= error.status_code < 500 and error.status_code != 409
    return ApplicationError(
        str(error),
        type=f"InternalApi{error.status_code}",
        non_retryable=non_retryable,
    )


def _require_output_string(output: dict[str, Any] | None, field_name: str) -> str:
    if output is None:
        raise ApplicationError(
            "Tool Gateway output is missing.",
            type="ToolGatewayOutput",
            non_retryable=True,
        )
    return _require_string(output, field_name)


def _require_string(value: dict[str, Any], field_name: str) -> str:
    field = value.get(field_name)
    if not isinstance(field, str) or not field:
        raise ApplicationError(
            f"{field_name} is missing from structured output.",
            type="StructuredOutputError",
            non_retryable=True,
        )
    return field
