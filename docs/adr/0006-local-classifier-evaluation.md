# ADR 0006: Frozen local classifier evaluation

- Status: Accepted
- Date: 2026-08-23

## Context

Manual exact-cluster triage produced useful KEEP and UNWANTED seed policy, but its leverage fell sharply once the largest remaining clusters were only about 50 messages. A classifier must be selected against this user's judgment before it is allowed to make dry-run recommendations for unknown sources. The most costly error is human KEEP classified as UNWANTED.

## Decision

Use a provider-neutral `IClusterClassifier` boundary with a loopback-only Ollama implementation for MAIL-003A. Freeze eligible exact-cluster evidence in SQLite, separately retain ground truth for scoring, and persist deterministic stratified development/validation/holdout assignments. Conditional and deferred decisions are excluded from binary ground truth.

Run one configured model profile at a time, one inference at a time. Ollama's typed `HttpClient` uses an infinite timeout so multi-minute HDD cold loads and inference are bounded by explicit run cancellation instead of the .NET 100-second default. Before a fresh measured profile, query `/api/ps` and unload the exact model if already resident. Persist each profile/item result, unload the active model before advancing, and resume without recomputing completed pairs; the first new request after resume is treated as another cold observation. Use Ollama structured output with an actual JSON Schema, then validate the parsed domain result again in the application. Thinking content is never persisted.

Lock the versioned prompt before holdout can run. Locking requires a completed development/validation run and persists the exact frozen corpus ID; later corpora cannot change V1's holdout. Treat high-confidence human KEEP → model UNWANTED on holdout as a failed safety gate. Otherwise, a holdout gate passes only after the run and profile complete with one valid result for every expected item and no request/schema failures; all other holdout states are incomplete. Do not automatically select a winner, classify unknown sources, or change human policy.

## Consequences

- Benchmarks remain reproducible even after later Gmail scans or policy edits.
- Ground truth cannot leak through the typed classifier input payload.
- HDD cold-load time is reported separately from steady-state inference.
- Missing models are reported and never pulled automatically.
- Local inference can take substantial time, so execution is a durable background job.
- MAIL-003B remains blocked on human review of the bakeoff.

## Safety boundary

The classifier receives local SQLite metadata and representative subjects only. It has no Gmail credentials, Gmail tools, browser, shell, cloud API, attachment contents, or operational authority. Gmail OAuth remains exactly `https://www.googleapis.com/auth/gmail.readonly`.
