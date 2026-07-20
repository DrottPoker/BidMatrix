import asyncio
import contextlib
import logging
import signal

from temporalio.client import Client

from bidmatrix_agents.health import HealthState, serve_health
from bidmatrix_agents.settings import WorkerSettings

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

    try:
        client = await connect_to_temporal(settings, state, stop_event)
        if client is None:
            return

        logger.info(
            "Agent worker host connected to Temporal namespace %s for task queue %s",
            settings.temporal_namespace,
            settings.temporal_task_queue,
        )
        await stop_event.wait()
    finally:
        state.temporal_connected = False
        state.temporal_detail = "Shutting down"
        health_task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await health_task


def main() -> None:
    asyncio.run(run())


if __name__ == "__main__":
    main()
