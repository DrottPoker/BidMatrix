from datetime import timedelta

from temporalio import workflow
from temporalio.common import RetryPolicy

with workflow.unsafe.imports_passed_through():
    from bidmatrix_agents.activities.analysis_intake import (
        create_manual_review_task,
        extract_analysis_documents,
        load_analysis_intake,
        mark_analysis_processing,
        mark_analysis_requires_review,
    )

from bidmatrix_agents.workflows.models import AnalysisIntakeInput, AnalysisIntakeResult


@workflow.defn(name="AnalysisIntakeWorkflow")
class AnalysisIntakeWorkflow:
    @workflow.run
    async def run(self, request: AnalysisIntakeInput) -> AnalysisIntakeResult:
        retry_policy = RetryPolicy(
            initial_interval=timedelta(seconds=1),
            backoff_coefficient=2.0,
            maximum_interval=timedelta(seconds=30),
            maximum_attempts=5,
        )
        activity_timeout = timedelta(seconds=20)

        state = await workflow.execute_activity(
            load_analysis_intake,
            request,
            start_to_close_timeout=activity_timeout,
            retry_policy=retry_policy,
        )
        if not state.file_scan_statuses:
            raise RuntimeError("Analysis intake requires at least one file record.")
        if any(
            status not in {"clean", "development_bypass"} for status in state.file_scan_statuses
        ):
            raise RuntimeError("Analysis intake encountered a disallowed file scan status.")

        await workflow.execute_activity(
            mark_analysis_processing,
            request,
            start_to_close_timeout=activity_timeout,
            retry_policy=retry_policy,
        )
        extraction_status = await workflow.execute_activity(
            extract_analysis_documents,
            request,
            start_to_close_timeout=timedelta(minutes=2),
            retry_policy=retry_policy,
        )
        if extraction_status not in {"succeeded", "partial"}:
            raise RuntimeError(f"Unexpected extraction status: {extraction_status}")
        review_task_id = await workflow.execute_activity(
            create_manual_review_task,
            request,
            start_to_close_timeout=activity_timeout,
            retry_policy=retry_policy,
        )
        await workflow.execute_activity(
            mark_analysis_requires_review,
            request,
            start_to_close_timeout=activity_timeout,
            retry_policy=retry_policy,
        )

        return AnalysisIntakeResult(
            analysis_id=request.analysis_id,
            review_task_id=review_task_id,
            status="requires_review",
        )
