# Extraction Prototype F1 release gate

## Scope gate

- Digital PDF extraction only.
- OCR remains unavailable and visible as `requires_ocr`.
- Every requirement remains pending human review.
- Company matching, scoring, outbound actions, billing, remote Git, and production deployment remain disabled.

## Static and automated verification

Run from the repository root:

```powershell
npm run lint
npm run typecheck
npm run test
npm run build
dotnet build BidMatrix.slnx --configuration Release --no-restore
dotnet test BidMatrix.slnx --configuration Release --no-build
$env:UV_CACHE_DIR = Join-Path $env:TEMP 'bidmatrix-uv-cache'
uv run --directory src/agents ruff check .
uv run --directory src/agents mypy
uv run --directory src/agents pytest
docker compose --env-file .env config --quiet
```

## Running-stack verification

```powershell
docker compose --env-file .env up --detach --build --wait
.\scripts\verify-f1.ps1 -OwnerEmail $env:OWNER_BOOTSTRAP_EMAIL -OwnerPassword $env:OWNER_BOOTSTRAP_PASSWORD
```

The running gate must prove:

- migration `0008_f1_document_extraction` applies;
- all six long-running services are healthy;
- the synthetic PDF is stored and processed by Temporal;
- extraction ends as `succeeded` and analysis ends as `requires_review`;
- at least two mandatory requirements are extracted;
- every extracted requirement has a citation;
- no fixture file requires OCR or fails extraction;
- the F0 audit, draft-only, disabled-external, and engineering-sandbox controls still pass.

## Manual inspection

- Open the analysis detail page.
- Confirm that document type, page count, mandatory status, confidence, review status, file name, page number, and exact quote are visible.
- Confirm that no compliance score, company match, bid/no-bid result, or legal conclusion is shown.
- Confirm that the page remains usable when requirements are empty or a file is marked `requires_ocr`.
