# ADR 0003: List-ID grouping with a compact Sent interaction index

- Status: Accepted
- Date: 2026-08-19

## Context

Bulk senders often vary the visible From address while preserving `List-ID`. Personal and transactional mail may have no `List-ID`. The dashboard also needs to distinguish senders and threads the mailbox owner has engaged with without mixing Sent messages into received volume.

## Decision

Normalize a `List-ID` by extracting the angle-bracket identifier, trimming it, and lowercasing it. When present, use `list:{identifier}` as the group key. Otherwise parse and lowercase the sender address and use `sender:{address}`.

Scan Sent separately and persist only Gmail message ID, thread ID, normalized recipient address, date, and scan bookkeeping. After both passes, mark a received message when either:

- its normalized sender appears among Sent recipients; or
- its Gmail thread ID appears in Sent.

Dashboard `RelationshipCount` counts received messages satisfying either signal.

## Consequences

- Mailing-list volume remains stable across From-name/address variations.
- Direct correspondence is visible without displaying Sent subjects or content.
- The relationship signal is intentionally heuristic: forwarding and alias use can create false positives or negatives.
