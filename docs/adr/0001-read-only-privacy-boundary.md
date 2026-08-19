# ADR 0001: Read-only Gmail and metadata-only persistence boundary

- Status: Accepted
- Date: 2026-08-19

## Context

The census needs mailbox-wide message attributes and attachment presence without gaining the ability to mutate the mailbox or retaining message content. Gmail's `METADATA` format returns IDs, labels, and headers, but not the MIME part tree needed for reliable attachment detection.

## Decision

Request exactly `GmailService.Scope.GmailReadonly`. The application exposes only list and get operations through `IGmailMailboxClient`; no modifying Gmail request type is used.

For each listed message:

1. Request `format=METADATA` with only `From`, `Reply-To`, `To`, `Cc`, `Bcc`, `Subject`, `Date`, `List-ID`, and `List-Unsubscribe`.
2. After metadata succeeds, request `format=FULL` with a partial-response `fields` selector limited to MIME filenames and `attachmentId` values. The selector excludes `body.data`.
3. Derive the attachment boolean in memory and discard the response object.

Never call the attachment download endpoint. Persist no body, HTML, raw MIME, snippet, token, or OAuth credential field. Log only operational state and sanitized failure classifications.

## Consequences

- The installed app cannot change or send mail, even if application logic is compromised.
- Attachment presence costs a second get request per message, but body bytes and attachments are not transferred in the partial response.
- `gmail.readonly` is a Google restricted scope and can require verification outside a personal/test deployment.
- A prompt-injection `.eml` fixture remains useful safety corpus but is never interpreted or persisted as content.
