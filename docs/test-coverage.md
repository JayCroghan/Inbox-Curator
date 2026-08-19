# Test coverage report

- Run date: 2026-08-19
- Target: .NET 10 (`net10.0`)
- Test runner: xUnit through Microsoft.NET.Test.Sdk
- Collector: coverlet XPlat Code Coverage (Cobertura)
- Result: **21 passed, 0 failed, 0 skipped**

## Coverage

| Metric | Covered | Valid | Rate |
| --- | ---: | ---: | ---: |
| Lines | 1,037 | 1,296 | **80.01%** |
| Branches | 291 | 460 | **63.26%** |

The denominator includes generated Razor Page classes and source-generated regular-expression code. Gmail's live installed-app/browser authorization and real network transport are deliberately not exercised by the offline suite; the OAuth scope, transient-status classification, delay curve, metadata mapping, and scanner behavior are tested at their local boundaries.

## Covered scenarios

- List-ID extraction, sender normalization, and recipient deduplication
- List-ID-first grouping and sender fallback
- Label, unsubscribe, promotion, importance, and attachment flags
- Prompt-injection fixture remains inert and absent from the persistence model
- OAuth scope is exactly `gmail.readonly`
- Exponential retry timing and retryable HTTP status classification
- Sent address/thread relationship reconciliation
- Idempotent replay and stale-row pruning after a completed rescan
- Failure checkpoint and resume from the last durable page token
- Bounded concurrent page fetching, including the configured limit, the 32-worker clamp, sibling cancellation, and failed-page replay
- Dashboard aggregation, representative subjects, search, sort, and paging
- EF Core migration/startup, synthetic seeding, Razor rendering, and response security headers

## Reproduce

```powershell
dotnet build InboxCurator.slnx
dotnet test InboxCurator.slnx --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults
```

The raw report is emitted as `artifacts/TestResults/{run-id}/coverage.cobertura.xml`.
