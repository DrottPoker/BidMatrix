# F1 extraction evaluation

## Evaluation set

`tests/fixtures/rfp/f1-evaluation-set.json` is the manually annotated F1 evaluation set. It contains three synthetic procurement documents, five mandatory requirements, optional statements, non-requirement background text, expected document types, expected normalized requirement text, and expected page numbers.

The set contains no customer data and is deterministic.

## Metrics

The automated evaluation calculates:

- mandatory requirement recall;
- exact mandatory result count, which detects false mandatory classifications in the fixture;
- document classification correctness;
- citation page correctness;
- quote equality between the extracted requirement and its citation.

The F1 release threshold is:

- mandatory recall: 100% on `f1-eval-v1`;
- document classifications: 100% on `f1-eval-v1`;
- every detected requirement has a file and page citation;
- no critical miss may be hidden or replaced with a confidence-only claim.

This small synthetic set proves pipeline behavior, not production accuracy. Real pilot documents and correction capture belong to F2.

## Command

```powershell
dotnet test BidMatrix.slnx --configuration Release --filter RequirementExtractionEvaluationTests
```
