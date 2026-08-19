# ADR 0002: Page-boundary checkpoints and idempotent upserts

- Status: Accepted
- Date: 2026-08-19

## Context

Large Gmail mailboxes require many API calls and may encounter throttling, network interruption, or process shutdown. Replaying work must not duplicate records, and a failed partial scan must not erase records it has not reached yet.

## Decision

Use one `ScanCheckpoint` for the Sent pass and one for the Census pass. A page is the durability boundary:

1. List one Gmail page.
2. Fetch metadata/structure for every message in that page with a bounded worker pool.
3. Upsert the page and update `NextPageToken` in one SQLite transaction.

Messages are unique by Gmail message ID. Sent interactions are unique by Gmail message ID plus normalized recipient. A retry can safely replay the last uncommitted page.

The worker count is configured by `Gmail:MaxConcurrentMessageFetches`, defaults to 12, and is clamped to 1–32. Concurrency changes only the in-memory fetch stage: persistence and checkpoint advancement still begin only after every fetch in the page has succeeded. A non-recovered fetch failure cancels sibling page work where practical and leaves the previous page token durable.

Every fresh pass receives an `ActiveRunId`; a resumed pass retains it. On successful completion, rows not marked with that run ID are deleted. Failure retains both the page token and run ID and performs no pruning.

Retry `429`, `500`, `502`, `503`, and `504` responses with `2^attempt` seconds plus jitter, capped at 60 seconds and a configured attempt limit.

## Consequences

- Progress between pages survives restarts.
- SQLite page transactions prevent a checkpoint from advancing ahead of data.
- Bounded per-page concurrency reduces Gmail round-trip latency without unbounded task fan-out.
- A completed rescan reflects removals and moves into excluded folders.
- A page can be fetched again after interruption, trading some API work for simple, auditable correctness.
