from http import HTTPStatus

from bidmatrix_agents.health import HealthState, build_health_payload


def test_liveness_is_independent_of_temporal() -> None:
    status, payload = build_health_payload("/health/live", HealthState())

    assert status == HTTPStatus.OK
    assert payload == {"status": "healthy"}


def test_readiness_requires_temporal_connection() -> None:
    state = HealthState()

    disconnected_status, _ = build_health_payload("/health/ready", state)
    state.temporal_connected = True
    state.temporal_detail = "Connected"
    state.api_connected = True
    state.api_detail = "Connected"
    connected_status, payload = build_health_payload("/health/ready", state)

    assert disconnected_status == HTTPStatus.SERVICE_UNAVAILABLE
    assert connected_status == HTTPStatus.OK
    assert payload["checks"] == {
        "temporal": {"connected": True, "detail": "Connected"},
        "api": {"connected": True, "detail": "Connected"},
    }
