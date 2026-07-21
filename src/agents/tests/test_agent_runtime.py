import json
from pathlib import Path

import pytest
from pydantic import ValidationError

from bidmatrix_agents.agent_runtime.adapters import validate_tool_permissions
from bidmatrix_agents.agent_runtime.contracts import OUTPUT_MODELS
from bidmatrix_agents.agent_runtime.fake_agents import run_fake_agent
from bidmatrix_agents.settings import WorkerSettings

FIXTURES = Path(__file__).parents[3] / "tests" / "fixtures" / "agents"

ALLOWED_TOOLS = {
    "executive": [
        "context.getCompanyConstitution",
        "context.getMetricsSnapshot",
        "knowledge.search",
        "task.create",
        "artifact.createDraft",
        "approval.request",
    ],
    "support": [
        "context.getProductFacts",
        "context.getAnalysis",
        "knowledge.search",
        "artifact.createDraft",
        "task.create",
        "approval.request",
    ],
    "product-analyst": [
        "context.getMetricsSnapshot",
        "knowledge.search",
        "task.create",
        "artifact.createDraft",
        "approval.request",
    ],
    "engineering": [
        "repo.readFile",
        "repo.search",
        "repo.getStatus",
        "repo.createWorktree",
        "repo.writeFile",
        "repo.runAllowlistedCommand",
        "repo.getDiff",
        "repo.createDiffArtifact",
        "artifact.createDraft",
        "approval.request",
    ],
}


@pytest.mark.parametrize(
    ("agent_key", "fixture_name"),
    [
        ("executive", "executive.json"),
        ("support", "support-prompt-injection.json"),
        ("product-analyst", "product-analyst.json"),
        ("engineering", "engineering.json"),
    ],
)
def test_each_fake_agent_has_valid_deterministic_output(
    agent_key: str,
    fixture_name: str,
) -> None:
    input_data = json.loads((FIXTURES / fixture_name).read_text(encoding="utf-8"))

    first = run_fake_agent(agent_key, input_data)
    second = run_fake_agent(agent_key, input_data)

    assert first == second
    assert OUTPUT_MODELS[agent_key].model_validate(first.model_dump()) == first
    validate_tool_permissions(first, ALLOWED_TOOLS[agent_key])


def test_prompt_injection_does_not_gain_email_tool() -> None:
    input_data = json.loads(
        (FIXTURES / "support-prompt-injection.json").read_text(encoding="utf-8")
    )

    output = run_fake_agent("support", input_data)

    assert all(action.tool_key != "email.send" for action in output.proposed_actions)
    assert any("ignored" in uncertainty for uncertainty in output.uncertainties)
    validate_tool_permissions(output, ALLOWED_TOOLS["support"])


def test_invalid_structured_input_is_rejected() -> None:
    with pytest.raises(ValidationError):
        run_fake_agent("support", {"conversation": "not-a-list"})


def test_live_mode_is_explicit_and_requires_all_configuration(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("INTERNAL_SERVICE_TOKEN", "test-token")
    monkeypatch.setenv("BIDMATRIX_AGENT_MODE", "deterministic")
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)

    deterministic = WorkerSettings()

    assert deterministic.agent_mode == "deterministic"
    assert deterministic.model_for_agent("support") == "support-deterministic-f0"

    monkeypatch.setenv("BIDMATRIX_AGENT_MODE", "live")
    with pytest.raises(ValidationError, match="OPENAI_API_KEY"):
        WorkerSettings()
