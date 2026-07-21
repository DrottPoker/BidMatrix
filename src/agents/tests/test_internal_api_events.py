import json

import pytest

from bidmatrix_agents.internal_api import (
    ClaimedEvent,
    parse_agent_task_event,
    parse_analysis_submitted_event,
    parse_approval_decided_event,
    parse_approval_requested_event,
)


def test_versioned_events_map_to_deterministic_workflow_inputs() -> None:
    analysis_event = build_event(
        "analysis.submitted.v1",
        {
            "analysisId": "analysis-1",
            "workflowId": "analysis-intake-analysis-1",
        },
    )
    approval_event = build_event(
        "approval.requested.v1",
        {
            "approvalId": "approval-1",
            "workflowId": "approval-approval-1",
            "expiresAt": "2030-01-01T00:00:00+00:00",
        },
    )
    decision_event = build_event(
        "approval.decided.v1",
        {
            "approvalId": "approval-1",
            "workflowId": "approval-approval-1",
            "status": "approved",
            "executionStatus": "disabled",
        },
    )

    analysis_workflow_id, analysis_input = parse_analysis_submitted_event(analysis_event)
    approval_workflow_id, approval_input = parse_approval_requested_event(approval_event)
    decision_workflow_id, decision = parse_approval_decided_event(decision_event)

    assert analysis_workflow_id == "analysis-intake-analysis-1"
    assert analysis_input.analysis_id == "analysis-1"
    assert approval_workflow_id == "approval-approval-1"
    assert approval_input.approval_id == "approval-1"
    assert decision_workflow_id == approval_workflow_id
    assert decision.status == "approved"
    assert decision.execution_status == "disabled"


def test_event_without_versioned_payload_is_rejected() -> None:
    event = ClaimedEvent(
        eventId="event-1",
        eventType="approval.requested.v1",
        aggregateId="approval-1",
        payload=json.dumps({"schemaVersion": 1}),
        attemptCount=1,
    )

    with pytest.raises(ValueError, match="versioned payload"):
        parse_approval_requested_event(event)


def test_agent_task_event_preserves_role_and_workflow_identity() -> None:
    event = build_event(
        "agent.task.created.v1",
        {
            "taskId": "task-1",
            "agentKey": "support",
            "workflowId": "agent-task-task-1",
        },
    )

    workflow_id, workflow_input = parse_agent_task_event(event)

    assert workflow_id == "agent-task-task-1"
    assert workflow_input.task_id == "task-1"
    assert workflow_input.agent_key == "support"


def build_event(event_type: str, payload: dict[str, object]) -> ClaimedEvent:
    return ClaimedEvent(
        eventId=f"event-{event_type}",
        eventType=event_type,
        aggregateId="aggregate-1",
        payload=json.dumps(
            {
                "schemaVersion": 1,
                "organizationId": "organization-1",
                "correlationId": "correlation-1",
                "payload": payload,
            }
        ),
        attemptCount=1,
    )
