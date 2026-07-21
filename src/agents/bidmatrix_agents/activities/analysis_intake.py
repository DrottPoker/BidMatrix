from collections.abc import Awaitable, Callable

from temporalio import activity
from temporalio.exceptions import ApplicationError

from bidmatrix_agents.internal_api import InternalApiClient, InternalApiError
from bidmatrix_agents.settings import WorkerSettings
from bidmatrix_agents.workflows.models import AnalysisIntakeInput, AnalysisIntakeState

type ApiCall[T] = Callable[[], Awaitable[T]]


async def _run_api_call[T](call: ApiCall[T]) -> T:
    try:
        return await call()
    except InternalApiError as error:
        non_retryable = 400 <= error.status_code < 500 and error.status_code != 409
        raise ApplicationError(
            str(error),
            type=f"InternalApi{error.status_code}",
            non_retryable=non_retryable,
        ) from error


@activity.defn(name="load_analysis_intake")
async def load_analysis_intake(request: AnalysisIntakeInput) -> AnalysisIntakeState:
    settings = WorkerSettings()
    async with InternalApiClient(settings) as client:
        return await _run_api_call(lambda: client.load_analysis_intake(request))


@activity.defn(name="mark_analysis_processing")
async def mark_analysis_processing(request: AnalysisIntakeInput) -> None:
    settings = WorkerSettings()
    async with InternalApiClient(settings) as client:
        await _run_api_call(lambda: client.mark_processing(request))


@activity.defn(name="extract_analysis_documents")
async def extract_analysis_documents(request: AnalysisIntakeInput) -> str:
    settings = WorkerSettings()
    async with InternalApiClient(settings) as client:
        return await _run_api_call(lambda: client.extract_analysis(request))


@activity.defn(name="create_manual_review_task")
async def create_manual_review_task(request: AnalysisIntakeInput) -> str:
    settings = WorkerSettings()
    async with InternalApiClient(settings) as client:
        return await _run_api_call(lambda: client.create_manual_review_task(request))


@activity.defn(name="mark_analysis_requires_review")
async def mark_analysis_requires_review(request: AnalysisIntakeInput) -> None:
    settings = WorkerSettings()
    async with InternalApiClient(settings) as client:
        await _run_api_call(lambda: client.mark_requires_review(request))
