using System.Text;
using System.Text.Json;
using InboxCurator.Data;

namespace InboxCurator.Classification;

public sealed record ClassifierNormalizationAttempt(
    ValidatedClassifierOutput? Output,
    string? Explanation,
    IReadOnlyList<string> RawReasonCodes,
    IReadOnlyList<string> Warnings,
    string? FailureCode);

public static class ClassifierOutputNormalizer
{
    private static readonly IReadOnlyDictionary<string, ClassifierRecommendation> Recommendations =
        new Dictionary<string, ClassifierRecommendation>(StringComparer.Ordinal)
        {
            ["keep"] = ClassifierRecommendation.Keep,
            ["unwanted"] = ClassifierRecommendation.Unwanted,
            ["needs_review"] = ClassifierRecommendation.NeedsReview,
            ["needsreview"] = ClassifierRecommendation.NeedsReview
        };

    private static readonly IReadOnlyDictionary<string, ClassifierConfidence> Confidences =
        BuildMap<ClassifierConfidence>();
    private static readonly IReadOnlyDictionary<string, ClassifierCategory> Categories =
        BuildMap<ClassifierCategory>();
    private static readonly IReadOnlyDictionary<string, ClassifierReasonCode> ReasonCodes =
        BuildMap<ClassifierReasonCode>();

    private static readonly HashSet<ClassifierCategory> NoiseCategories =
    [
        ClassifierCategory.Marketing,
        ClassifierCategory.Newsletter,
        ClassifierCategory.MailingList
    ];

    private static readonly HashSet<ClassifierCategory> ProtectedCategories =
    [
        ClassifierCategory.Personal,
        ClassifierCategory.Professional,
        ClassifierCategory.Transactional,
        ClassifierCategory.AccountSecurity,
        ClassifierCategory.Finance,
        ClassifierCategory.GovernmentLegal,
        ClassifierCategory.Health,
        ClassifierCategory.Travel,
        ClassifierCategory.PurchaseReceipt,
        ClassifierCategory.TechnicalNotification
    ];

    private static readonly HashSet<ClassifierReasonCode> NoiseReasons =
    [
        ClassifierReasonCode.PromotionalContent,
        ClassifierReasonCode.NewsletterContent,
        ClassifierReasonCode.BulkMail,
        ClassifierReasonCode.HighFrequency,
        ClassifierReasonCode.StaleSource,
        ClassifierReasonCode.NoRelationship
    ];

    private static readonly HashSet<ClassifierReasonCode> ProtectedReasons =
    [
        ClassifierReasonCode.RelationshipSignal,
        ClassifierReasonCode.StarredOrImportant,
        ClassifierReasonCode.TransactionalContent,
        ClassifierReasonCode.SecurityContent,
        ClassifierReasonCode.FinancialOrLegalContent,
        ClassifierReasonCode.TravelOrBookingContent,
        ClassifierReasonCode.ReceiptOrOrderContent,
        ClassifierReasonCode.ProfessionalContent
    ];

    public static ClassifierNormalizationAttempt NormalizePrimary(string json) => Normalize(json, requireExplanation: true);

    public static ClassifierNormalizationAttempt NormalizeRepair(string json) => Normalize(json, requireExplanation: false);

    public static IReadOnlyList<string> SemanticWarnings(ValidatedClassifierOutput output)
    {
        var warnings = new List<string>(2);
        var hasProtectedReason = output.ReasonCodes.Any(ProtectedReasons.Contains);
        var hasNoiseReason = output.ReasonCodes.Any(NoiseReasons.Contains);
        if (output.Recommendation == ClassifierRecommendation.Keep &&
            NoiseCategories.Contains(output.Category) &&
            !hasProtectedReason &&
            output.ReasonCodes.All(NoiseReasons.Contains))
        {
            warnings.Add("keep_with_only_noise_signals");
        }

        if (output.Recommendation == ClassifierRecommendation.Unwanted &&
            (ProtectedCategories.Contains(output.Category) || hasProtectedReason) &&
            !NoiseCategories.Contains(output.Category) &&
            !hasNoiseReason)
        {
            warnings.Add("unwanted_with_only_protected_signals");
        }

        return warnings;
    }

    private static ClassifierNormalizationAttempt Normalize(string json, bool requireExplanation)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new ClassifierNormalizationAttempt(null, null, [], ["invalid_json"], "invalid_json");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ClassifierNormalizationAttempt(null, null, [], ["invalid_json_object"], "invalid_json_object");
            }

            var root = document.RootElement;
            var warnings = new List<string>();
            var explanation = ReadString(root, "explanation") ?? ReadString(root, "rationale");
            if (requireExplanation && string.IsNullOrWhiteSpace(explanation))
            {
                warnings.Add("explanation_missing");
            }

            var confidence = ClassifierConfidence.Low;
            var confidenceValue = ReadString(root, "confidence");
            if (confidenceValue is null || !Confidences.TryGetValue(NormalizeToken(confidenceValue), out confidence))
            {
                confidence = ClassifierConfidence.Low;
                warnings.Add("confidence_defaulted");
            }

            var category = ClassifierCategory.Unknown;
            var categoryValue = ReadString(root, "category");
            if (categoryValue is null || !Categories.TryGetValue(NormalizeToken(categoryValue), out category))
            {
                category = ClassifierCategory.Unknown;
                warnings.Add("category_defaulted");
            }

            var rawReasonCodes = ReadReasonCodes(root, warnings);
            var normalizedReasonCodes = new List<ClassifierReasonCode>();
            var seenReasonCodes = new HashSet<ClassifierReasonCode>();
            var hasUnknownReasonCode = false;
            foreach (var rawReasonCode in rawReasonCodes)
            {
                if (!ReasonCodes.TryGetValue(NormalizeToken(rawReasonCode), out var reasonCode))
                {
                    hasUnknownReasonCode = true;
                    continue;
                }

                if (!seenReasonCodes.Add(reasonCode))
                {
                    continue;
                }

                normalizedReasonCodes.Add(reasonCode);
            }

            if (rawReasonCodes.Count == 0)
            {
                warnings.Add("reason_codes_missing");
            }

            if (rawReasonCodes.Count > 5)
            {
                warnings.Add("reason_code_count_exceeds_five");
            }

            if (rawReasonCodes.Select(NormalizeToken).Distinct(StringComparer.Ordinal).Count() != rawReasonCodes.Count)
            {
                warnings.Add("reason_codes_deduplicated");
            }

            if (hasUnknownReasonCode)
            {
                warnings.Add("unknown_reason_codes_retained");
            }

            var recommendationValue = ReadString(root, "recommendation");
            if (recommendationValue is null)
            {
                warnings.Add("recommendation_missing");
                return new ClassifierNormalizationAttempt(
                    null, explanation, rawReasonCodes, warnings, "recommendation_missing");
            }

            if (!Recommendations.TryGetValue(NormalizeToken(recommendationValue), out var recommendation))
            {
                warnings.Add("recommendation_unrecognized");
                return new ClassifierNormalizationAttempt(
                    null, explanation, rawReasonCodes, warnings, "recommendation_unrecognized");
            }

            return new ClassifierNormalizationAttempt(
                new ValidatedClassifierOutput(
                    recommendation,
                    confidence,
                    category,
                    normalizedReasonCodes,
                    explanation ?? string.Empty),
                explanation,
                rawReasonCodes,
                warnings,
                null);
        }
    }

    private static List<string> ReadReasonCodes(JsonElement root, List<string> warnings)
    {
        if (!TryGetProperty(root, "reasonCodes", out var reasonCodes))
        {
            return [];
        }

        if (reasonCodes.ValueKind != JsonValueKind.Array)
        {
            warnings.Add("reason_codes_not_array");
            return [];
        }

        return reasonCodes.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => item is not null)
            .Cast<string>()
            .ToList();
    }

    private static string? ReadString(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static IReadOnlyDictionary<string, TEnum> BuildMap<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(value => NormalizeToken(value.ToString()), value => value, StringComparer.Ordinal);

    private static string NormalizeToken(string value)
    {
        var result = new StringBuilder(value.Length);
        var pendingSeparator = false;
        var previousWasLowerOrDigit = false;
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                if ((pendingSeparator || (char.IsUpper(character) && previousWasLowerOrDigit)) && result.Length > 0)
                {
                    result.Append('_');
                }

                result.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
                previousWasLowerOrDigit = char.IsLower(character) || char.IsDigit(character);
            }
            else
            {
                pendingSeparator = true;
                previousWasLowerOrDigit = false;
            }
        }

        return result.ToString();
    }
}
