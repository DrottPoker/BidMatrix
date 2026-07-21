import asyncio
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol

from agents import Agent, Runner

from bidmatrix_agents.agent_runtime.contracts import (
    OUTPUT_MODELS,
    AgentOutput,
)
from bidmatrix_agents.agent_runtime.fake_agents import run_fake_agent
from bidmatrix_agents.settings import WorkerSettings


@dataclass(frozen=True)
class AgentUsage:
    request_count: int
    input_tokens: int
    output_tokens: int
    reasoning_tokens: int | None


@dataclass(frozen=True)
class AdapterResult:
    output: AgentOutput
    usage: AgentUsage


class AgentModelAdapter(Protocol):
    async def run(
        self,
        agent_key: str,
        input_data: dict[str, Any],
        model_name: str,
        max_turns: int,
    ) -> AdapterResult: ...


class DeterministicFakeAdapter:
    async def run(
        self,
        agent_key: str,
        input_data: dict[str, Any],
        model_name: str,
        max_turns: int,
    ) -> AdapterResult:
        del model_name, max_turns
        output = run_fake_agent(agent_key, input_data)
        serialized_input = json.dumps(input_data, sort_keys=True, separators=(",", ":"))
        serialized_output = output.model_dump_json()
        return AdapterResult(
            output=output,
            usage=AgentUsage(
                request_count=1,
                input_tokens=max(len(serialized_input) // 4, 1),
                output_tokens=max(len(serialized_output) // 4, 1),
                reasoning_tokens=0,
            ),
        )


class OpenAIAgentsAdapter:
    async def run(
        self,
        agent_key: str,
        input_data: dict[str, Any],
        model_name: str,
        max_turns: int,
    ) -> AdapterResult:
        output_model = OUTPUT_MODELS[agent_key]
        agent: Agent[None] = Agent(
            name=f"BidMatrix {agent_key}",
            instructions=load_prompt(agent_key),
            model=model_name,
            output_type=output_model,
            tools=[],
        )
        result = await Runner.run(
            agent,
            json.dumps(input_data, sort_keys=True),
            max_turns=max_turns,
        )
        output = output_model.model_validate(result.final_output)
        usage = result.context_wrapper.usage
        return AdapterResult(
            output=output,
            usage=AgentUsage(
                request_count=usage.requests,
                input_tokens=usage.input_tokens,
                output_tokens=usage.output_tokens,
                reasoning_tokens=usage.output_tokens_details.reasoning_tokens,
            ),
        )


async def run_configured_adapter(
    settings: WorkerSettings,
    agent_key: str,
    input_data: dict[str, Any],
) -> AdapterResult:
    adapter: AgentModelAdapter = (
        DeterministicFakeAdapter()
        if settings.agent_mode == "deterministic"
        else OpenAIAgentsAdapter()
    )
    result = await asyncio.wait_for(
        adapter.run(
            agent_key,
            input_data,
            settings.model_for_agent(agent_key),
            settings.agent_max_turns,
        ),
        timeout=settings.agent_timeout_seconds,
    )
    return result


def validate_tool_permissions(
    output: AgentOutput,
    allowed_tools: list[str],
) -> None:
    allowed = set(allowed_tools)
    disallowed = sorted(
        action.tool_key for action in output.proposed_actions if action.tool_key not in allowed
    )
    if disallowed:
        raise ValueError(f"Agent output proposed disallowed tools: {', '.join(disallowed)}")


def load_prompt(agent_key: str) -> str:
    prompt_path = Path(__file__).parents[1] / "prompts" / agent_key.replace("-", "_") / "v1.md"
    return prompt_path.read_text(encoding="utf-8")
