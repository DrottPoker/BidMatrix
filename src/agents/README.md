# BidMatrix agent worker

This Python 3.14 package hosts the Temporal worker, versioned prompts, strict Pydantic contracts, deterministic fixture models, optional OpenAI Agents SDK adapter, and authenticated internal API client.

Deterministic mode is the F0 default and requires no OpenAI key. It implements offline demonstrations for Executive, Support, Product Analyst, and Engineering roles. Every proposed action is checked against the active role inventory and materialized through the ASP.NET Core Tool Gateway.

```powershell
uv sync --project src/agents --locked
uv run --directory src/agents ruff check .
uv run --directory src/agents mypy
uv run --directory src/agents pytest
```

Live mode is explicit opt-in and requires `OPENAI_API_KEY` plus a model setting for every role. The Python process never connects directly to the application database and cannot approve owner actions.
