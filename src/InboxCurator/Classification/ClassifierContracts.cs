using System.Text.Json;
using System.Text.Json.Serialization;
using InboxCurator.Data;

namespace InboxCurator.Classification;

public sealed record ClusterClassifierInput(
    [property: JsonPropertyName("targetType")] string TargetType,
    [property: JsonPropertyName("targetValue")] string TargetValue,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("grouping")] string Grouping,
    [property: JsonPropertyName("messageCount")] int MessageCount,
    [property: JsonPropertyName("firstReceivedUtc")] DateTime FirstReceivedUtc,
    [property: JsonPropertyName("lastReceivedUtc")] DateTime LastReceivedUtc,
    [property: JsonPropertyName("unreadCount")] int UnreadCount,
    [property: JsonPropertyName("starredCount")] int StarredCount,
    [property: JsonPropertyName("importantCount")] int ImportantCount,
    [property: JsonPropertyName("relationshipCount")] int RelationshipCount,
    [property: JsonPropertyName("promotionsCount")] int PromotionsCount,
    [property: JsonPropertyName("promotionsPercentage")] double PromotionsPercentage,
    [property: JsonPropertyName("listUnsubscribeCount")] int ListUnsubscribeCount,
    [property: JsonPropertyName("listUnsubscribePercentage")] double ListUnsubscribePercentage,
    [property: JsonPropertyName("attachmentCount")] int AttachmentCount,
    [property: JsonPropertyName("attachmentPercentage")] double AttachmentPercentage,
    [property: JsonPropertyName("representativeSubjects")] IReadOnlyList<string> RepresentativeSubjects)
{
    [JsonPropertyName("unreadPercentage")]
    public double UnreadPercentage => Percentage(UnreadCount, MessageCount);

    [JsonPropertyName("starredPercentage")]
    public double StarredPercentage => Percentage(StarredCount, MessageCount);

    [JsonPropertyName("importantPercentage")]
    public double ImportantPercentage => Percentage(ImportantCount, MessageCount);

    [JsonPropertyName("relationshipPercentage")]
    public double RelationshipPercentage => Percentage(RelationshipCount, MessageCount);

    private static double Percentage(int count, int total) =>
        total == 0 ? 0 : Math.Round(count * 100d / total, 1, MidpointRounding.AwayFromZero);
}

public sealed record ClassifierPromptSnapshot(
    string Version,
    string SystemPrompt,
    string SystemPromptSha256,
    string OutputSchemaVersion,
    string OutputJsonSchema,
    ClassifierResponseProtocol ResponseProtocol = ClassifierResponseProtocol.StrictV1,
    string? RepairPromptVersion = null,
    string? RepairSystemPrompt = null,
    string? RepairSystemPromptSha256 = null,
    string? RepairOutputSchemaVersion = null,
    string? RepairOutputJsonSchema = null);

public sealed record ClassifierModelProfile(
    string Key,
    string Model,
    string? Think,
    double Temperature,
    int NumCtx,
    bool Stream,
    string KeepAlive);

public sealed record ValidatedClassifierOutput(
    ClassifierRecommendation Recommendation,
    ClassifierConfidence Confidence,
    ClassifierCategory Category,
    IReadOnlyList<ClassifierReasonCode> ReasonCodes,
    string Rationale);

public sealed record ClassifierResponseMetrics(
    long? TotalDurationNanoseconds,
    long? LoadDurationNanoseconds,
    int? PromptEvalCount,
    long? PromptEvalDurationNanoseconds,
    int? EvalCount,
    long? EvalDurationNanoseconds,
    bool ThinkingPresent,
    int ThinkingCharacterCount);

public sealed record ClusterClassifierResponse(
    ValidatedClassifierOutput? Output,
    string PrimaryResponse,
    string? RepairResponse,
    string? PrimaryExplanation,
    IReadOnlyList<string> RawReasonCodes,
    ClassifierNormalizationMode NormalizationMode,
    IReadOnlyList<string> NormalizationWarnings,
    IReadOnlyList<string> SemanticWarnings,
    string? NormalizationFailureCode,
    string? RepairFailureCode,
    ClassifierResponseMetrics PrimaryMetrics,
    ClassifierResponseMetrics? RepairMetrics);

public interface IClusterClassifier
{
    Task<ClusterClassifierResponse> ClassifyAsync(
        ClassifierModelProfile profile,
        ClassifierPromptSnapshot prompt,
        ClusterClassifierInput input,
        CancellationToken cancellationToken);
}

public interface ILocalModelRuntime
{
    Task<IReadOnlySet<string>> GetInstalledModelsAsync(CancellationToken cancellationToken);
    Task<ModelResidency?> GetResidencyAsync(string model, CancellationToken cancellationToken);
    Task UnloadAsync(string model, CancellationToken cancellationToken);
}

public sealed record ModelResidency(string Model, long SizeBytes, long SizeVramBytes, int ContextLength)
{
    public double VramPercentage => SizeBytes <= 0 ? 0 : Math.Clamp(SizeVramBytes * 100d / SizeBytes, 0, 100);
    public bool PredominantlyVramResident => VramPercentage >= 90;
}

public static class ClassifierInputFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ClusterClassifierInput Create(EvaluationCorpusItem item)
    {
        var subjects = JsonSerializer.Deserialize<string[]>(item.RepresentativeSubjectsJson, JsonOptions) ?? [];
        return new ClusterClassifierInput(
            item.TargetType == ClusterTargetType.ListId ? "list_id" : "sender",
            item.TargetValue,
            item.DisplayName,
            item.TargetType == ClusterTargetType.ListId ? "List-ID" : "Sender",
            item.MessageCount,
            item.FirstReceivedUtc,
            item.LastReceivedUtc,
            item.UnreadCount,
            item.StarredCount,
            item.ImportantCount,
            item.RelationshipCount,
            item.PromotionsCount,
            Percentage(item.PromotionsCount, item.MessageCount),
            item.ListUnsubscribeCount,
            Percentage(item.ListUnsubscribeCount, item.MessageCount),
            item.AttachmentCount,
            Percentage(item.AttachmentCount, item.MessageCount),
            subjects);
    }

    public static string Serialize(ClusterClassifierInput input) => JsonSerializer.Serialize(input, JsonOptions);

    private static double Percentage(int count, int total) =>
        total == 0 ? 0 : Math.Round(count * 100d / total, 1, MidpointRounding.AwayFromZero);
}

public static class ClassifierOutputValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly IReadOnlyDictionary<string, ClassifierRecommendation> Recommendations =
        new Dictionary<string, ClassifierRecommendation>(StringComparer.Ordinal)
        {
            ["keep"] = ClassifierRecommendation.Keep,
            ["unwanted"] = ClassifierRecommendation.Unwanted,
            ["needs_review"] = ClassifierRecommendation.NeedsReview
        };

    private static readonly IReadOnlyDictionary<string, ClassifierConfidence> Confidences =
        new Dictionary<string, ClassifierConfidence>(StringComparer.Ordinal)
        {
            ["high"] = ClassifierConfidence.High,
            ["medium"] = ClassifierConfidence.Medium,
            ["low"] = ClassifierConfidence.Low
        };

    private static readonly IReadOnlyDictionary<string, ClassifierCategory> Categories = BuildMap<ClassifierCategory>();
    private static readonly IReadOnlyDictionary<string, ClassifierReasonCode> ReasonCodes = BuildMap<ClassifierReasonCode>();

    public static ValidatedClassifierOutput Parse(string json)
    {
        OutputPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<OutputPayload>(json, JsonOptions)
                ?? throw new ClassifierSchemaException("empty_output");
        }
        catch (JsonException exception)
        {
            throw new ClassifierSchemaException("invalid_json", exception);
        }

        if (!Recommendations.TryGetValue(payload.Recommendation, out var recommendation) ||
            !Confidences.TryGetValue(payload.Confidence, out var confidence) ||
            !Categories.TryGetValue(payload.Category, out var category))
        {
            throw new ClassifierSchemaException("invalid_enum");
        }

        if (payload.ReasonCodes is null || payload.ReasonCodes.Length is < 1 or > 5 ||
            payload.ReasonCodes.Distinct(StringComparer.Ordinal).Count() != payload.ReasonCodes.Length)
        {
            throw new ClassifierSchemaException("invalid_reason_codes");
        }

        var mappedReasonCodes = new List<ClassifierReasonCode>(payload.ReasonCodes.Length);
        foreach (var reasonCode in payload.ReasonCodes)
        {
            if (!ReasonCodes.TryGetValue(reasonCode, out var mapped))
            {
                throw new ClassifierSchemaException("invalid_reason_code");
            }

            mappedReasonCodes.Add(mapped);
        }

        if (string.IsNullOrWhiteSpace(payload.Rationale) || payload.Rationale.Length > 240 ||
            payload.Rationale.Contains('\r', StringComparison.Ordinal) || payload.Rationale.Contains('\n', StringComparison.Ordinal))
        {
            throw new ClassifierSchemaException("invalid_rationale");
        }

        return new ValidatedClassifierOutput(recommendation, confidence, category, mappedReasonCodes, payload.Rationale);
    }

    public static string ReasonCodeValue(ClassifierReasonCode value) => ToSnakeCase(value.ToString());

    private static IReadOnlyDictionary<string, TEnum> BuildMap<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(value => ToSnakeCase(value.ToString()), value => value, StringComparer.Ordinal);

    private static string ToSnakeCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    private sealed class OutputPayload
    {
        [JsonPropertyName("recommendation")]
        public required string Recommendation { get; init; }

        [JsonPropertyName("confidence")]
        public required string Confidence { get; init; }

        [JsonPropertyName("category")]
        public required string Category { get; init; }

        [JsonPropertyName("reasonCodes")]
        public required string[] ReasonCodes { get; init; }

        [JsonPropertyName("rationale")]
        public required string Rationale { get; init; }
    }
}

public sealed class ClassifierSchemaException(string code, Exception? innerException = null)
    : InvalidOperationException(code, innerException)
{
    public string Code { get; } = code;
}
