using System.Text.Json;
using InboxCurator.Data;
using InboxCurator.Gmail;
using InboxCurator.Services;

namespace InboxCurator.Scanning;

public static class MessageMetadataMapper
{
    public static MessageRecord ToRecord(GmailMessageMetadata metadata, DateTime indexedAtUtc, string scanRunId)
    {
        var sender = AddressNormalizer.ParseSingle(Header(metadata, "From"));
        var replyToValue = Header(metadata, "Reply-To");
        var replyTo = string.IsNullOrWhiteSpace(replyToValue) ? null : AddressNormalizer.ParseSingle(replyToValue).Normalized;
        var listId = AddressNormalizer.NormalizeListId(Header(metadata, "List-ID"));
        var labels = metadata.LabelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new MessageRecord
        {
            GmailMessageId = metadata.MessageId,
            ThreadId = metadata.ThreadId,
            DateUtc = DateTime.SpecifyKind(metadata.DateUtc, DateTimeKind.Utc),
            LabelIdsJson = JsonSerializer.Serialize(metadata.LabelIds.Order(StringComparer.Ordinal).ToArray()),
            SenderName = sender.DisplayName,
            SenderAddress = sender.Original,
            NormalizedSenderAddress = sender.Normalized,
            ReplyToAddress = replyTo,
            Subject = Header(metadata, "Subject")?.Trim() ?? string.Empty,
            ListId = listId,
            HasListUnsubscribe = metadata.Headers.ContainsKey("List-Unsubscribe"),
            HasAttachment = metadata.HasAttachment,
            IsUnread = labels.Contains("UNREAD"),
            IsStarred = labels.Contains("STARRED"),
            IsImportant = labels.Contains("IMPORTANT"),
            IsPromotion = labels.Contains("CATEGORY_PROMOTIONS"),
            GroupKey = listId is null ? $"sender:{sender.Normalized}" : $"list:{listId}",
            GroupDisplay = listId ?? sender.DisplayName ?? sender.Normalized,
            GroupKind = listId is null ? "Sender" : "List-ID",
            LastSeenScanId = scanRunId,
            IndexedAtUtc = indexedAtUtc
        };
    }

    public static IReadOnlyCollection<SentInteraction> ToSentInteractions(GmailMessageMetadata metadata, string scanRunId)
    {
        return AddressNormalizer.ParseMany(Header(metadata, "To"), Header(metadata, "Cc"), Header(metadata, "Bcc"))
            .Select(address => new SentInteraction
            {
                GmailMessageId = metadata.MessageId,
                ThreadId = metadata.ThreadId,
                RecipientAddress = address,
                DateUtc = DateTime.SpecifyKind(metadata.DateUtc, DateTimeKind.Utc),
                LastSeenScanId = scanRunId
            })
            .ToArray();
    }

    private static string? Header(GmailMessageMetadata metadata, string name) =>
        metadata.Headers.TryGetValue(name, out var value) ? value : null;
}
