namespace InboxCurator.Data;

public enum ClusterTargetType
{
    ListId,
    Sender
}

public enum ClusterDecisionKind
{
    KeepProtect,
    UnwantedExistingAndFuture,
    CleanExistingOnly,
    CleanOlderThan,
    Defer
}

public enum ClusterDecisionChangeKind
{
    Created,
    Replaced,
    Removed
}

public sealed class ClusterDecision
{
    public long Id { get; set; }
    public ClusterTargetType TargetType { get; set; }
    public required string TargetValue { get; set; }
    public required string GroupKey { get; set; }
    public ClusterDecisionKind DecisionKind { get; set; }
    public DateTime? CutoffDateUtc { get; set; }
    public bool AppliesToFuture { get; set; }
    public bool IsActive { get; set; }
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<ClusterDecisionAudit> AuditEntries { get; } = [];
}

public sealed class ClusterDecisionAudit
{
    public long Id { get; set; }
    public long ClusterDecisionId { get; set; }
    public ClusterDecision ClusterDecision { get; set; } = null!;
    public ClusterDecisionChangeKind ChangeKind { get; set; }
    public ClusterDecisionKind DecisionKind { get; set; }
    public DateTime? CutoffDateUtc { get; set; }
    public bool AppliesToFuture { get; set; }
    public bool IsActive { get; set; }
    public int Revision { get; set; }
    public int MatchingMessageCount { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
