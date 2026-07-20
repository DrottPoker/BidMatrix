# ADR 0002: Use a modular monolith and a separate agent worker

- Status: Accepted
- Date: 2026-07-20

## Context

F0 needs clear domain boundaries without the operational and consistency costs of many network services. Agent execution has a different runtime, dependency set, and trust profile from the authoritative API.

## Decision

Deploy the ASP.NET Core application as a modular monolith with explicit modules. Run Python agent and workflow code in a separate worker process. The worker communicates with the application only through authenticated internal APIs.

## Consequences

- Modules remain independently understandable while sharing one application deployment.
- Cross-module changes use application interfaces rather than direct controller coupling.
- The worker can be isolated and scaled independently.
- Network contracts between the API and worker require contract tests.
