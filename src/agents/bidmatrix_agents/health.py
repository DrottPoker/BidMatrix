import asyncio
import json
from dataclasses import dataclass
from http import HTTPStatus
from typing import Any


@dataclass(slots=True)
class HealthState:
    temporal_connected: bool = False
    temporal_detail: str = "Connection pending"


def build_health_payload(path: str, state: HealthState) -> tuple[HTTPStatus, dict[str, Any]]:
    if path == "/health/live":
        return HTTPStatus.OK, {"status": "healthy"}

    if path == "/health/ready":
        status = HTTPStatus.OK if state.temporal_connected else HTTPStatus.SERVICE_UNAVAILABLE
        return status, {
            "status": "healthy" if state.temporal_connected else "notReady",
            "checks": {
                "temporal": {
                    "connected": state.temporal_connected,
                    "detail": state.temporal_detail,
                }
            },
        }

    return HTTPStatus.NOT_FOUND, {"status": "notFound"}


async def handle_health_request(
    reader: asyncio.StreamReader,
    writer: asyncio.StreamWriter,
    *,
    state: HealthState,
) -> None:
    try:
        request = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), timeout=2.0)
        request_line = request.split(b"\r\n", maxsplit=1)[0].decode("ascii")
        method, path, _ = request_line.split(" ", maxsplit=2)
        if method != "GET":
            status, payload = HTTPStatus.METHOD_NOT_ALLOWED, {"status": "methodNotAllowed"}
        else:
            status, payload = build_health_payload(path, state)
    except (TimeoutError, asyncio.IncompleteReadError, asyncio.LimitOverrunError, ValueError):
        status, payload = HTTPStatus.BAD_REQUEST, {"status": "badRequest"}

    body = json.dumps(payload, separators=(",", ":")).encode("utf-8")
    response = (
        f"HTTP/1.1 {status.value} {status.phrase}\r\n"
        "Content-Type: application/json\r\n"
        f"Content-Length: {len(body)}\r\n"
        "Connection: close\r\n"
        "\r\n"
    ).encode("ascii")
    writer.write(response + body)
    await writer.drain()
    writer.close()
    await writer.wait_closed()


async def serve_health(state: HealthState, host: str, port: int) -> None:
    server = await asyncio.start_server(
        lambda reader, writer: handle_health_request(reader, writer, state=state),
        host,
        port,
        limit=8_192,
    )
    async with server:
        await server.serve_forever()
