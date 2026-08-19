using InboxCurator.Data;

namespace InboxCurator.Gmail;

public sealed record GmailPage(IReadOnlyList<string> MessageIds, string? NextPageToken);

public sealed record GmailMessageMetadata(
    string MessageId,
    string ThreadId,
    DateTime DateUtc,
    IReadOnlyList<string> LabelIds,
    IReadOnlyDictionary<string, string> Headers,
    bool HasAttachment);

public interface IGmailMailboxClient
{
    Task<GmailPage> ListAsync(ScanKind kind, string? pageToken, CancellationToken cancellationToken);
    Task<GmailMessageMetadata> GetMetadataAsync(string messageId, CancellationToken cancellationToken);
}
