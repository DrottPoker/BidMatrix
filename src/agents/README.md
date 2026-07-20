# BidMatrix agent worker

This package hosts the separate Python process for Temporal workflows and agent orchestration. In Phase 1 it exposes health endpoints and verifies its Temporal connection. No model invocation or business workflow is enabled yet.

## Local setup

```powershell
uv sync
uv run pytest
uv run ruff check .
uv run mypy
```

The worker runs without an OpenAI API key. Live model mode remains opt-in in later phases.
