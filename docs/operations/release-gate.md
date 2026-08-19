# Current local release gate

The current release gate is the Hosted Concierge S1 gate in `docs/operations/s1-release-gate.md`.

Run the complete local static and running-stack verification from the repository root. The essential live command is:

```powershell
.\scripts\verify-f2.ps1 -EnvFile .env.example -ComposeProjectName bidmatrix-s1
.\scripts\verify-s1.ps1 -EnvFile .env.example -ComposeProjectName bidmatrix-s1
.\scripts\verify-s1-identity.ps1 -EnvFile .env.example -ComposeProjectName bidmatrix-s1
```

Use it only after starting a healthy stack under the same Compose project name. Prefer a disposable project as documented in the F2 runbook so verification does not depend on existing data.

The older F0, F1, and F2 runbooks remain historical regression contracts. They are not sufficient for the current SaaS foundation by themselves.

Passing the local gate does not prove a GitHub Actions run, cloud deployment, production security, commercial validation, or agent authorization.
