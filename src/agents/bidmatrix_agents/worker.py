import asyncio
import contextlib
import logging
import signal

from temporalio.client import Client
from temporalio.worker import Worker

from bidmatrix_agents.activities.agent_task import (
    fail_agent_task,
    materialize_agent_output,
    prepare_agent_task,
    run_structured_agent,
)
from bidmatrix_agents.activities.analysis_intake import (
    create_manual_review_task,
    load_analysis_intake,
    mark_analysis_processing,
    mark_analysis_requires_review,
)
from bidmatrix_agents.activities.approval import expire_approval
from bidmatrix_agents.event_bridge import run_event_bridge
from bidmatrix_agents.health import HealthState, serve_health
from bidmatrix_agents.settings import WorkerSettings
from bidmatrix_agents.workflows.agent_tasks import (
    AgentTaskWorkflow,
    DailyExecutiveBriefWorkflow,
    EngineeringTaskWorkflow,
    ProductReviewWorkflow,
    SupportDraftWorkflow,
)
from bidmatrix_agents.workflows.analysis_intake import AnalysisIntakeWorkflow
from bidmatrix_agents.workflows.approval import ApprovalWorkflow

logger = logging.getLogger(__name__)


async def connect_to_temporal(
    settings: WorkerSettings,
    state: HealthState,
    stop_event: asyncio.Event,
) -> Client | None:
    retry_delay = 0.5

    while not stop_event.is_set():
        try:
            client = await asyncio.wait_for(
                Client.connect(
                    settings.temporal_address,
                    namespace=settings.temporal_namespace,
                ),
                timeout=10.0,
            )
            state.temporal_connected = True
            state.temporal_detail = "Connected"
            return client
        except (TimeoutError, OSError, RuntimeError) as error:
            state.temporal_connected = False
            state.temporal_detail = type(error).__name__
            logger.warning("Temporal connection failed; retrying in %.1f seconds", retry_delay)
            try:
                await asyncio.wait_for(stop_event.wait(), timeout=retry_delay)
            except TimeoutError:
                retry_delay = min(retry_delay * 2, 5.0)

    return None


async def run() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )
    settings = WorkerSettings()
    state = HealthState()
    stop_event = asyncio.Event()
    loop = asyncio.get_running_loop()

    for shutdown_signal in (signal.SIGINT, signal.SIGTERM):
        with contextlib.suppress(NotImplementedError):
            loop.add_signal_handler(shutdown_signal, stop_event.set)

    health_task = asyncio.create_task(
        serve_health(state, settings.health_host, settings.health_port),
        name="health-server",
    )

    worker: Worker | None = None
    worker_task: asyncio.Task[None] | None = None
    bridge_task: asyncio.Task[None] | None = None
    try:
        client = await connect_to_temporal(settings, state, stop_event)
        if client is None:
            return

        worker = Worker(
            client,
            task_queue=settings.temporal_task_queue,
            workflows=[
                AnalysisIntakeWorkflow,
                ApprovalWorkflow,
                AgentTaskWorkflow,
                DailyExecutiveBriefWorkflow,
                SupportDraftWorkflow,
                ProductReviewWorkflow,
                EngineeringTaskWorkflow,
            ],
            activities=[
                load_analysis_intake,
                mark_analysis_processing,
                create_manual_review_task,
                mark_analysis_requires_review,
                expire_approval,
                prepare_agent_task,
                run_structured_agent,
                materialize_agent_output,
                fail_agent_task,
            ],
        )
        worker_task = asyncio.create_task(worker.run(), name="temporal-worker")
        bridge_task = asyncio.create_task(
            run_event_bridge(client, settings, state, stop_event),
            name="outbox-event-bridge",
        )

        logger.info(
            "Agent worker started in Temporal namespace %s on task queue %s",
            settings.temporal_namespace,
            settings.temporal_task_queue,
        )
        await stop_event.wait()
    finally:
        state.temporal_connected = False
        state.temporal_detail = "Shutting down"
        state.api_connected = False
        state.api_detail = "Shutting down"

        if bridge_task is not None:
            bridge_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await bridge_task
        if worker is not None:
            await worker.shutdown()
        if worker_task is not None:
            await worker_task

        health_task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await health_task


def main() -> None:
    asyncio.run(run())


if __name__ == "__main__":
    main()
