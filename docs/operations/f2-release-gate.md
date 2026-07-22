# Concierge Pilot F2 release gate

## Scope gate

- Customer results remain hidden until owner publication.
- Every visible item has an exact file, page, and quote citation.
- Owner corrections preserve the original extraction and increment a version.
- Rejected items do not appear in the customer report.
- No company match, score, bid or no-bid recommendation, billing, external action, cloud deployment, or production claim is present.

## Static and automated verification

Run from the repository root:

```powershell
npm run lint
npm run typecheck
npm run test
npm run build
dotnet build BidMatrix.slnx --configuration Release --no-restore
dotnet test BidMatrix.slnx --configuration Release --no-build
uv run --directory src/agents ruff check .
uv run --directory src/agents mypy
uv run --directory src/agents pytest
docker compose --env-file .env config --quiet
```

## Running-stack verification

1. Start the local stack with `docker compose --env-file .env up --detach --build --wait`.
2. Sign in as the bootstrapped owner.
3. Create an analysis in `/app/analyses/new` and upload the controlled test RFP.
4. Confirm that the customer sees processing or quality review but no extracted results.
5. Open `/owner/analyses`, review at least one item, and publish the analysis.
6. Return to the customer report and verify all five result sections.

The running gate must prove:

- migration `0009_f2_reviewable_results` applies;
- key dates, requested documents, and evaluation criteria are persisted with exact citations;
- customer results remain empty before publication;
- owner review writes are version checked and audited;
- publication changes the analysis to `completed` and exposes only non-rejected results;
- the customer report shows the publication note and quality-reviewed state;
- F0 tenancy, authentication, audit, and disabled-external controls still pass.

## Visual inspection

- Verify desktop and mobile navigation.
- Verify dashboard, empty states, loading states, errors, analysis list filters, upload progress, processing state, and published report.
- Verify requirements, dates, requested documents, evaluation criteria, and citations at narrow and wide viewports.
- Verify keyboard focus, readable contrast, and reduced-motion behavior.

## Commercial gate

- Record actual processing time and corrections for every pilot analysis.
- Secure at least one paid pilot.
- Measure how many pilot customers request another analysis.
- Do not call the market validated from implementation or test results alone.
