FROM ghcr.io/astral-sh/uv:0.11.12 AS uv

FROM python:3.14.6-slim-bookworm AS runtime
WORKDIR /app

ENV PATH="/app/.venv/bin:$PATH"
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV UV_COMPILE_BYTECODE=1
ENV UV_LINK_MODE=copy

RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=uv /uv /usr/local/bin/uv
COPY src/agents/pyproject.toml src/agents/uv.lock ./
RUN uv sync --frozen --no-dev --no-install-project

COPY src/agents/bidmatrix_agents ./bidmatrix_agents
COPY src/agents/README.md ./README.md
RUN uv sync --frozen --no-dev

EXPOSE 8081
CMD ["python", "-m", "bidmatrix_agents.worker"]
