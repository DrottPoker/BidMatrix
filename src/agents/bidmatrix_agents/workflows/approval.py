from datetime import UTC, datetime, timedelta

from temporalio import workflow
from temporalio.common import RetryPolicy

with workflow.unsafe.imports_passed_through():
    from bidmatrix_agents.activities.approval import expire_approval

from bidmatrix_agents.workflows.models import (
    ApprovalDecision,
    ApprovalWorkflowInput,
    ApprovalWorkflowResult,
)


@workflow.defn(name="ApprovalWorkflow")
class ApprovalWorkflow:
    def __init__(self) -> None:
        self._decision: ApprovalDecision | None = None

    @workflow.signal(name="record_decision")
    def record_decision(self, decision: ApprovalDecision) -> None:
        if self._decision is None:
            self._decision = decision

    @workflow.run
    async def run(self, request: ApprovalWorkflowInput) -> ApprovalWorkflowResult:
        expires_at = datetime.fromisoformat(request.expires_at.replace("Z", "+00:00"))
        if expires_at.tzinfo is None:
            expires_at = expires_at.replace(tzinfo=UTC)
        remaining = max((expires_at - workflow.now()).total_seconds(), 0.0)

        try:
            await workflow.wait_condition(
                lambda: self._decision is not None,
                timeout=timedelta(seconds=remaining),
            )
        except TimeoutError:
            self._decision = await workflow.execute_activity(
                expire_approval,
                request.approval_id,
                start_to_close_timeout=timedelta(seconds=20),
                retry_policy=RetryPolicy(
                    initial_interval=timedelta(seconds=1),
                    backoff_coefficient=2.0,
                    maximum_interval=timedelta(seconds=30),
                    maximum_attempts=5,
                ),
            )

        decision = self._decision
        if decision is None:
            raise RuntimeError("Approval workflow ended without a decision.")
        return ApprovalWorkflowResult(
            approval_id=request.approval_id,
            status=decision.status,
            execution_status=decision.execution_status,
        )
