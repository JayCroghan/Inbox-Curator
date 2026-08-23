namespace InboxCurator.Data;

public enum EvaluationGroundTruth
{
    Keep,
    Unwanted
}

public enum EvaluationSplit
{
    Development,
    Validation,
    Holdout
}

public enum ClassifierRunStage
{
    DevelopmentValidation,
    Holdout
}

public enum ClassifierRunState
{
    Queued,
    Running,
    CancelRequested,
    Cancelled,
    Completed,
    Failed
}

public enum ClassifierProfileState
{
    Pending,
    Running,
    Completed,
    Missing,
    Cancelled
}

public enum ClassifierResultStatus
{
    Completed,
    SchemaFailure,
    RequestFailure
}

public enum ClassifierResponseProtocol
{
    StrictV1,
    NormalizeRepairV2
}

public enum ClassifierNormalizationMode
{
    Direct,
    SelfRepaired,
    Failed
}

public enum ClassifierRecommendation
{
    Keep,
    Unwanted,
    NeedsReview
}

public enum ClassifierConfidence
{
    High,
    Medium,
    Low
}

public enum ClassifierCategory
{
    Personal,
    Professional,
    Transactional,
    AccountSecurity,
    Finance,
    GovernmentLegal,
    Health,
    Travel,
    PurchaseReceipt,
    TechnicalNotification,
    SocialNotification,
    Newsletter,
    Marketing,
    MailingList,
    Mixed,
    Unknown
}

public enum ClassifierReasonCode
{
    RelationshipSignal,
    StarredOrImportant,
    TransactionalContent,
    SecurityContent,
    FinancialOrLegalContent,
    TravelOrBookingContent,
    ReceiptOrOrderContent,
    ProfessionalContent,
    PromotionalContent,
    NewsletterContent,
    BulkMail,
    HighFrequency,
    StaleSource,
    NoRelationship,
    MixedContent,
    InsufficientEvidence
}

public sealed class EvaluationCorpus
{
    public long Id { get; set; }
    public required string Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public required string SplitStrategy { get; set; }
    public int EligibleSourceCount { get; set; }
    public int ExcludedCleanExistingOnlyCount { get; set; }
    public int ExcludedCleanOlderThanCount { get; set; }
    public int ExcludedDeferCount { get; set; }
    public int ExcludedMissingEvidenceCount { get; set; }
    public ICollection<EvaluationCorpusItem> Items { get; } = [];
    public ICollection<ClassifierRun> Runs { get; } = [];
    public ICollection<ClassifierPromptVersion> LockedPromptVersions { get; } = [];
}

public sealed class EvaluationCorpusItem
{
    public long Id { get; set; }
    public long EvaluationCorpusId { get; set; }
    public EvaluationCorpus EvaluationCorpus { get; set; } = null!;
    public ClusterTargetType TargetType { get; set; }
    public required string TargetValue { get; set; }
    public required string GroupKey { get; set; }
    public required string DisplayName { get; set; }
    public EvaluationGroundTruth GroundTruth { get; set; }
    public EvaluationSplit Split { get; set; }
    public int MessageCount { get; set; }
    public DateTime FirstReceivedUtc { get; set; }
    public DateTime LastReceivedUtc { get; set; }
    public int UnreadCount { get; set; }
    public int StarredCount { get; set; }
    public int ImportantCount { get; set; }
    public int RelationshipCount { get; set; }
    public int PromotionsCount { get; set; }
    public int ListUnsubscribeCount { get; set; }
    public int AttachmentCount { get; set; }
    public required string RepresentativeSubjectsJson { get; set; }
    public ICollection<ClassifierResult> ClassifierResults { get; } = [];
}

public sealed class ClassifierPromptVersion
{
    public long Id { get; set; }
    public required string Version { get; set; }
    public required string SystemPrompt { get; set; }
    public required string SystemPromptSha256 { get; set; }
    public required string OutputSchemaVersion { get; set; }
    public required string OutputJsonSchema { get; set; }
    public string? OutputJsonSchemaSha256 { get; set; }
    public ClassifierResponseProtocol ResponseProtocol { get; set; }
    public string? RepairPromptVersion { get; set; }
    public string? RepairSystemPrompt { get; set; }
    public string? RepairSystemPromptSha256 { get; set; }
    public string? RepairOutputSchemaVersion { get; set; }
    public string? RepairOutputJsonSchema { get; set; }
    public string? RepairOutputJsonSchemaSha256 { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public long? LockedEvaluationCorpusId { get; set; }
    public EvaluationCorpus? LockedEvaluationCorpus { get; set; }
    public ICollection<ClassifierRun> Runs { get; } = [];
}

public sealed class ClassifierRun
{
    public required string Id { get; set; }
    public long EvaluationCorpusId { get; set; }
    public EvaluationCorpus EvaluationCorpus { get; set; } = null!;
    public long ClassifierPromptVersionId { get; set; }
    public ClassifierPromptVersion ClassifierPromptVersion { get; set; } = null!;
    public ClassifierRunStage Stage { get; set; }
    public ClassifierRunState State { get; set; }
    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public int FailedItems { get; set; }
    public string? CurrentProfileKey { get; set; }
    public EvaluationSplit? CurrentSplit { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelRequestedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public ICollection<ClassifierRunProfile> Profiles { get; } = [];
    public ICollection<ClassifierResult> Results { get; } = [];
}

public sealed class ClassifierRunProfile
{
    public long Id { get; set; }
    public required string ClassifierRunId { get; set; }
    public ClassifierRun ClassifierRun { get; set; } = null!;
    public required string ProfileKey { get; set; }
    public required string ModelName { get; set; }
    public string? ThinkMode { get; set; }
    public double Temperature { get; set; }
    public int ContextLength { get; set; }
    public bool Stream { get; set; }
    public required string KeepAlive { get; set; }
    public int ExecutionOrder { get; set; }
    public ClassifierProfileState State { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? ColdLoadDurationNanoseconds { get; set; }
    public long? ModelSizeBytes { get; set; }
    public long? SizeVramBytes { get; set; }
    public int? RuntimeContextLength { get; set; }
    public bool? PredominantlyVramResident { get; set; }
    public ICollection<ClassifierResult> Results { get; } = [];
}

public sealed class ClassifierResult
{
    public long Id { get; set; }
    public required string ClassifierRunId { get; set; }
    public ClassifierRun ClassifierRun { get; set; } = null!;
    public long ClassifierRunProfileId { get; set; }
    public ClassifierRunProfile ClassifierRunProfile { get; set; } = null!;
    public long EvaluationCorpusItemId { get; set; }
    public EvaluationCorpusItem EvaluationCorpusItem { get; set; } = null!;
    public ClassifierResultStatus Status { get; set; }
    public ClassifierRecommendation? Recommendation { get; set; }
    public ClassifierConfidence? Confidence { get; set; }
    public ClassifierCategory? Category { get; set; }
    public string? ReasonCodesJson { get; set; }
    public string? Rationale { get; set; }
    public string? PrimaryResponse { get; set; }
    public string? PrimaryExplanation { get; set; }
    public string? RawReasonCodesJson { get; set; }
    public ClassifierNormalizationMode? NormalizationMode { get; set; }
    public string? NormalizationWarningsJson { get; set; }
    public string? SemanticWarningsJson { get; set; }
    public string? RepairFailureCode { get; set; }
    public string? FailureCode { get; set; }
    public bool ThinkingPresent { get; set; }
    public int ThinkingCharacterCount { get; set; }
    public bool IsColdLoadRequest { get; set; }
    public long? TotalDurationNanoseconds { get; set; }
    public long? LoadDurationNanoseconds { get; set; }
    public int? PromptEvalCount { get; set; }
    public long? PromptEvalDurationNanoseconds { get; set; }
    public int? EvalCount { get; set; }
    public long? EvalDurationNanoseconds { get; set; }
    public long? RepairTotalDurationNanoseconds { get; set; }
    public long? RepairLoadDurationNanoseconds { get; set; }
    public int? RepairPromptEvalCount { get; set; }
    public long? RepairPromptEvalDurationNanoseconds { get; set; }
    public int? RepairEvalCount { get; set; }
    public long? RepairEvalDurationNanoseconds { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
