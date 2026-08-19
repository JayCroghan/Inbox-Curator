# ADR 0005: Audited human policy on exact sender/List-ID clusters

- Status: Accepted
- Date: 2026-08-19

## Context

The first real census indexed 30,283 messages from 3,632 sources. The top 24 clusters accounted for approximately 40.2% of the mailbox, confirming that a small number of source-level decisions can classify substantial historical volume. The same brand or domain can emit distinct streams with different histories and desired handling.

MAIL-002 remains Gmail read-only, but later phases need durable, explainable seed policy rather than transient UI state.

## Decision

Persist one current `ClusterDecision` per exact normalized target:

- `ListId` targets match normalized `Messages.ListId`;
- `Sender` targets match normalized sender only where List-ID fallback grouping applies;
- target type and target value are explicit, while `GroupKey` remains a convenient indexed reference;
- domain-wide targets are not supported.

Every create, replacement, and removal appends a `ClusterDecisionAudit` snapshot containing the revision, decision, cutoff, future-policy flag, active state, timestamp, and matching-message count. Removing a decision deactivates the current record rather than deleting its history.

`KeepProtect` and `UnwantedExistingAndFuture` apply to future mail. `CleanExistingOnly`, `CleanOlderThan`, and `Defer` do not. A cutoff is required only for `CleanOlderThan`. Review state and policy state are separate: any active decision, including `Defer`, marks a source reviewed; only a non-Defer decision supplies policy. Deferred sources therefore leave the unreviewed queue while contributing no policy coverage.

Age-rule controls always render the persisted cutoff as an explicit `Clean before <date>` value. Reopening an existing rule defaults to preserving that stored cutoff. A 30-day, 90-day, one-year, or custom shortcut deliberately resolves to and stores a replacement explicit date; the server accepts a preserve request only when the target already has an active age rule.

Coverage is calculated against current local metadata. Keep/protect and unwanted-existing-and-future cover every matching message. Clean-existing-only covers messages dated at or before that decision revision, so later mail remains outside the rule. Clean-older-than covers only messages before its cutoff. Explicit decisions remain visible even when relationship evidence exists.

## Consequences

- Human intent survives restarts and can be edited or reversed without erasing history.
- Dashboard coverage updates immediately after local decision changes.
- Later classifiers and executors can consume stable exact-cluster policy and audit provenance.
- A domain cannot accidentally collapse unrelated mailing streams into one rule.
- No Gmail mutation, body fetch, OAuth scope, or consent behavior is introduced.
