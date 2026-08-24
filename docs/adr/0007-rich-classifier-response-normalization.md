# ADR 0007: Rich classifier response normalization and self-repair

- Status: Accepted
- Date: 2026-08-24

## Context

The immutable V1 development/validation experiment produced useful free-form explanations but was dominated by strict schema failures, especially duplicate, excessive, or imperfect reason codes. Usable recommendations were discarded because diagnostic serialization was imperfect. Cold-load accounting also waited for a successfully validated result, causing a roughly four-minute Qwen HDD load to display as zero when the first response failed validation.

## Decision

Keep V1 and its runs unchanged. Add immutable `MAIL-003A-PROMPT-V2` / `MAIL-003A-OUTPUT-V2` plus extraction-only `MAIL-003A-REPAIR-V1`, with all prompt/schema text and hashes persisted.

V2 first requests a relaxed JSON object containing a recommendation and user-visible audit explanation. The complete primary final response is stored locally before its fields are normalized. Confidence, category, and reason-code imperfections are non-fatal: values are normalized case-insensitively, safe defaults and warnings are recorded, canonical reason codes are deduplicated, and raw reason values remain available for inspection.

Only an unusable recommendation triggers one repair call on the same resident model. Repair receives the primary response and repair contract only—never corpus evidence, ground truth, decisions, or another model result. It extracts only the candidate's declared recommendation without reconsidering mailbox evidence. Confidence, category, canonical/raw reason codes, warnings, and explanation come solely from primary normalization; invalid primary JSON supplies Low/Unknown/empty diagnostic defaults. Repair can therefore never elevate confidence or overwrite the explanation. An unresolved recommendation after that one attempt becomes a normalization failure.

The complete primary and repair responses and their separate metrics are persisted locally without thinking traces. The first returned primary response is the cold observation regardless of normalization outcome. Semantic contradictions are warnings and never silently rewrite the recommendation. Separate read-only inspection modes make repaired and warned results auditable even when their recommendation agrees with ground truth.

Development run creation requires an explicit persisted corpus ID and defaults to V2. Holdout execution and detailed holdout evidence remain disabled in MAIL-003A.1.

## Consequences

- The bakeoff measures model judgment rather than exact diagnostic-field formatting.
- Rich explanations and raw diagnostics remain available for human review.
- Repair cannot contaminate confidence-sensitive safety metrics.
- Repair overhead and dependence are measurable per profile.
- V1 results remain readable and directly distinguishable from V2.
- No Gmail authority, unknown-source behavior, or operational action changes.
