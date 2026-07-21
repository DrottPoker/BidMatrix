from temporalio import activity
from temporalio.exceptions import ApplicationError

from bidmatrix_agents.internal_api import InternalApiClient, InternalApiError
from bidmatrix_agents.settings import WorkerSettings
from bidmatrix_agents.workflows.models import ApprovalDecision


@activity.defn(name="expire_approval")
async def expire_approval(approval_id: str) -> ApprovalDecision:
    settings = WorkerSettings()
    try:
        async with InternalApiClient(settings) as client:
            return await client.expire_approval(approval_id)
    except InternalApiError as error:
        non_retryable = 400 <= error.status_code < 500 and error.status_code != 409
        raise ApplicationError(
            str(error),
            type=f"InternalApi{error.status_code}",
            non_retryable=non_retryable,
        ) from error
