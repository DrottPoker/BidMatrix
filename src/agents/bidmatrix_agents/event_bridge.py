import asyncio
import logging

import httpx
from temporalio.client import Client
from temporalio.exceptions import WorkflowAlreadyStartedError
from temporalio.service import RPCError

from bidmatrix_agents.health import HealthState
from bidmatrix_agents.internal_api import (
    InternalApiClient,
    InternalApiError,
    parse_agent_task_event,
    parse_analysis_submitted_event,
    parse_approval_decided_event,
    parse_approval_requested_event,
)
from bidmatrix_agents.settings import WorkerSettings
from bidmatrix_agents.workflows.agent_tasks import (
    DailyExecutiveBriefWorkflow,
    EngineeringTaskWorkflow,
    ProductReviewWorkflow,
    SupportDraftWorkflow,
)
from bidmatrix_agents.workflows.analysis_intake import AnalysisIntakeWorkflow
from bidmatrix_agents.workflows.approval import ApprovalWorkflow
from bidmatrix_agents.workflows.models import AgentTaskWorkflowInput

logger = logging.getLogger(__name__)


async def run_event_bridge(
    temporal_client: Client,
    settings: WorkerSettings,
    state: HealthState,
    stop_event: asyncio.Event,
) -> None:
    async with InternalApiClient(settings) as api_client:
        while not stop_event.is_set():
            try:
                analysis_events = await api_client.claim_events("analysis.submitted.v1")
                state.api_connected = True
                state.api_detail = "Connected"

                for event in analysis_events:
                    try:
                        analysis_workflow_id, analysis_input = parse_analysis_submitted_event(event)
                        await temporal_client.start_workflow(
                            AnalysisIntakeWorkflow.run,
                            analysis_input,
                            id=analysis_workflow_id,
                            task_queue=settings.temporal_task_queue,
                        )
                        await api_client.acknowledge_event(event.event_id)
                    except WorkflowAlreadyStartedError:
                        await api_client.acknowledge_event(event.event_id)
                    except (InternalApiError, ValueError, RuntimeError) as error:
                        logger.error(
                            "Analysis event %s could not start: %s",
                            event.event_id,
                            type(error).__name__,
                        )
                        await api_client.fail_event(event.event_id, type(error).__name__)

                requested_events = await api_client.claim_events("approval.requested.v1")
                for event in requested_events:
                    try:
                        approval_workflow_id, approval_input = parse_approval_requested_event(event)
                        await temporal_client.start_workflow(
                            ApprovalWorkflow.run,
                            approval_input,
                            id=approval_workflow_id,
                            task_queue=settings.temporal_task_queue,
                        )
                        await api_client.acknowledge_event(event.event_id)
                    except WorkflowAlreadyStartedError:
                        await api_client.acknowledge_event(event.event_id)
                    except (InternalApiError, ValueError, RuntimeError, RPCError) as error:
                        logger.error(
                            "Approval request event %s could not start: %s",
                            event.event_id,
                            type(error).__name__,
                        )
                        await api_client.fail_event(event.event_id, type(error).__name__)

                decided_events = await api_client.claim_events("approval.decided.v1")
                for event in decided_events:
                    try:
                        decision_workflow_id, decision = parse_approval_decided_event(event)
                        handle = temporal_client.get_workflow_handle(decision_workflow_id)
                        await handle.signal(ApprovalWorkflow.record_decision, decision)
                        await api_client.acknowledge_event(event.event_id)
                    except (InternalApiError, ValueError, RuntimeError, RPCError) as error:
                        logger.error(
                            "Approval decision event %s could not signal: %s",
                            event.event_id,
                            type(error).__name__,
                        )
                        await api_client.fail_event(event.event_id, type(error).__name__)

                agent_events = await api_client.claim_events("agent.task.created.v1")
                for event in agent_events:
                    try:
                        agent_workflow_id, agent_input = parse_agent_task_event(event)
                        await _start_agent_workflow(
                            temporal_client,
                            settings,
                            agent_workflow_id,
                            agent_input,
                        )
                        await api_client.acknowledge_event(event.event_id)
                    except WorkflowAlreadyStartedError:
                        await api_client.acknowledge_event(event.event_id)
                    except (InternalApiError, ValueError, RuntimeError, RPCError) as error:
                        logger.error(
                            "Agent task event %s could not start: %s",
                            event.event_id,
                            type(error).__name__,
                        )
                        await api_client.fail_event(event.event_id, type(error).__name__)
            except (httpx.HTTPError, InternalApiError) as error:
                state.api_connected = False
                state.api_detail = type(error).__name__
                logger.warning("Internal API event bridge is unavailable: %s", type(error).__name__)

            try:
                await asyncio.wait_for(
                    stop_event.wait(),
                    timeout=settings.event_poll_interval_seconds,
                )
            except TimeoutError:
                pass


async def _start_agent_workflow(
    client: Client,
    settings: WorkerSettings,
    workflow_id: str,
    request: AgentTaskWorkflowInput,
) -> None:
    if request.agent_key == "executive":
        await client.start_workflow(
            DailyExecutiveBriefWorkflow.run,
            request,
            id=workflow_id,
            task_queue=settings.temporal_task_queue,
        )
    elif request.agent_key == "support":
        await client.start_workflow(
            SupportDraftWorkflow.run,
            request,
            id=workflow_id,
            task_queue=settings.temporal_task_queue,
        )
    elif request.agent_key == "product-analyst":
        await client.start_workflow(
            ProductReviewWorkflow.run,
            request,
            id=workflow_id,
            task_queue=settings.temporal_task_queue,
        )
    elif request.agent_key == "engineering":
        await client.start_workflow(
            EngineeringTaskWorkflow.run,
            request,
            id=workflow_id,
            task_queue=settings.temporal_task_queue,
        )
    else:
        raise ValueError(f"Unknown agent key {request.agent_key}")
