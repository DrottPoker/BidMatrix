from datetime import timedelta

from temporalio import workflow
from temporalio.common import RetryPolicy
from temporalio.exceptions import ActivityError

with workflow.unsafe.imports_passed_through():
    from bidmatrix_agents.activities.agent_task import (
        fail_agent_task,
        materialize_agent_output,
        prepare_agent_task,
        run_structured_agent,
    )

from bidmatrix_agents.workflows.models import (
    AgentFailureInput,
    AgentMaterializationInput,
    AgentTaskWorkflowInput,
    AgentTaskWorkflowResult,
)


async def _run_agent_task(request: AgentTaskWorkflowInput) -> AgentTaskWorkflowResult:
    retry_policy = RetryPolicy(
        initial_interval=timedelta(seconds=1),
        backoff_coefficient=2.0,
        maximum_interval=timedelta(seconds=20),
        maximum_attempts=3,
    )
    preparation = None
    try:
        preparation = await workflow.execute_activity(
            prepare_agent_task,
            request,
            start_to_close_timeout=timedelta(seconds=20),
            retry_policy=retry_policy,
        )
        execution = await workflow.execute_activity(
            run_structured_agent,
            preparation,
            start_to_close_timeout=timedelta(minutes=3),
            retry_policy=retry_policy,
        )
        return await workflow.execute_activity(
            materialize_agent_output,
            AgentMaterializationInput(preparation=preparation, execution=execution),
            start_to_close_timeout=timedelta(minutes=2),
            retry_policy=retry_policy,
        )
    except ActivityError as error:
        await workflow.execute_activity(
            fail_agent_task,
            AgentFailureInput(
                workflow_input=request,
                agent_run_id=preparation.agent_run_id if preparation is not None else None,
                failure_code="structured_agent_failed",
                failure_message=type(error).__name__,
            ),
            start_to_close_timeout=timedelta(seconds=20),
            retry_policy=retry_policy,
        )
        raise


@workflow.defn(name="AgentTaskWorkflow")
class AgentTaskWorkflow:
    @workflow.run
    async def run(self, request: AgentTaskWorkflowInput) -> AgentTaskWorkflowResult:
        return await _run_agent_task(request)


@workflow.defn(name="DailyExecutiveBriefWorkflow")
class DailyExecutiveBriefWorkflow:
    @workflow.run
    async def run(self, request: AgentTaskWorkflowInput) -> AgentTaskWorkflowResult:
        return await _run_agent_task(request)


@workflow.defn(name="SupportDraftWorkflow")
class SupportDraftWorkflow:
    @workflow.run
    async def run(self, request: AgentTaskWorkflowInput) -> AgentTaskWorkflowResult:
        return await _run_agent_task(request)


@workflow.defn(name="ProductReviewWorkflow")
class ProductReviewWorkflow:
    @workflow.run
    async def run(self, request: AgentTaskWorkflowInput) -> AgentTaskWorkflowResult:
        return await _run_agent_task(request)


@workflow.defn(name="EngineeringTaskWorkflow")
class EngineeringTaskWorkflow:
    @workflow.run
    async def run(self, request: AgentTaskWorkflowInput) -> AgentTaskWorkflowResult:
        return await _run_agent_task(request)
