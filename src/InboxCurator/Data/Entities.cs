namespace InboxCurator.Data;

public sealed class MessageRecord
{
    public long Id { get; set; }
    public required string GmailMessageId { get; set; }
    public required string ThreadId { get; set; }
    public DateTime DateUtc { get; set; }
    public required string LabelIdsJson { get; set; }
    public string? SenderName { get; set; }
    public required string SenderAddress { get; set; }
    public required string NormalizedSenderAddress { get; set; }
    public string? ReplyToAddress { get; set; }
    public required string Subject { get; set; }
    public string? ListId { get; set; }
    public bool HasListUnsubscribe { get; set; }
    public bool HasAttachment { get; set; }
    public bool IsUnread { get; set; }
    public bool IsStarred { get; set; }
    public bool IsImportant { get; set; }
    public bool IsPromotion { get; set; }
    public bool HasDirectCorrespondence { get; set; }
    public bool HasThreadInteraction { get; set; }
    public required string GroupKey { get; set; }
    public required string GroupDisplay { get; set; }
    public required string GroupKind { get; set; }
    public required string LastSeenScanId { get; set; }
    public DateTime IndexedAtUtc { get; set; }
}

public sealed class SentInteraction
{
    public long Id { get; set; }
    public required string GmailMessageId { get; set; }
    public required string ThreadId { get; set; }
    public required string RecipientAddress { get; set; }
    public DateTime DateUtc { get; set; }
    public required string LastSeenScanId { get; set; }
}

public enum ScanKind
{
    Census,
    Sent
}

public enum ScanState
{
    NeverRun,
    Running,
    Completed,
    Failed
}

public sealed class ScanCheckpoint
{
    public long Id { get; set; }
    public ScanKind Kind { get; set; }
    public ScanState State { get; set; }
    public string? NextPageToken { get; set; }
    public long ProcessedCount { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? ActiveRunId { get; set; }
}
