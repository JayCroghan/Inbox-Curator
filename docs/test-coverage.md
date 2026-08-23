# Test coverage report

- Run date: 2026-08-23
- Target: .NET 10 (`net10.0`)
- Test runner: xUnit through Microsoft.NET.Test.Sdk
- Collector: coverlet XPlat Code Coverage (Cobertura)
- Result: **60 passed, 0 failed, 0 skipped**

## Coverage

| Metric | Covered | Valid | Rate |
| --- | ---: | ---: | ---: |
| Lines | 4,932 | 5,364 | **91.94%** |
| Branches | 1,043 | 1,408 | **74.07%** |

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
- All five human decision types, exact List-ID and sender targets, edit/remove history, future-policy flags, and cutoff validation
- Separate unreviewed, deferred, and policy-decided states, including default queue movement and zero-coverage Defer semantics
- Explicit age-cutoff rendering, preservation for existing shortcut/custom dates, deliberate replacement, and affected-count updates
- Opt-in triage scroll preservation markup plus exact path/query scoping, single-use consumption, and stale-state expiry behavior
- Coverage calculations, clean-existing temporal boundaries, relationship-evidence precedence, and triage filters
- Decision persistence through a new `DbContext`, the MAIL-002 migration, and local-only paged cluster detail
- EF Core migration/startup, synthetic seeding, Razor rendering, and response security headers
- Eligible ground-truth selection, conditional/deferred exclusions, frozen-evidence immutability, and absence of ground truth/body fields from classifier input
- Deterministic oldest/newest/time-distributed subject sampling and fixed-seed stratified development/validation/holdout assignment
- Prompt-injection subjects remain inert untrusted input under the immutable `MAIL-003A-PROMPT-V1` contract
- Strict structured-output enum/domain validation, including rejection of numeric confidence and unexpected fields
- Dangerous KEEP → UNWANTED, high-confidence safety gate, unwanted precision, abstention, coverage, and steady-state performance scoring
- Ollama installed-model checks without pulls, actual JSON Schema payloads, exact five-profile think modes, metrics, `/api/ps` residency, and `keep_alive=0` unload
- Model-at-a-time execution order, durable item failures, resume skipping, cancellation state, and explicit unload boundaries
- Locked-holdout web controls, synthetic safety scoreboard/disagreement rendering, no recommendation acceptance, and no Gmail calls from the classifier page

## Reproduce

```powershell
dotnet build InboxCurator.slnx --configuration Release
dotnet test InboxCurator.slnx --configuration Release --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/TestResults
```

The raw report is emitted as `artifacts/TestResults/{run-id}/coverage.cobertura.xml`.
