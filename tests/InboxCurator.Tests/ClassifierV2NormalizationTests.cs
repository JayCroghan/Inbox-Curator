using InboxCurator.Classification;
using InboxCurator.Data;

namespace InboxCurator.Tests;

public sealed class ClassifierV2NormalizationTests
{
    [Fact]
    public void DuplicateAndExcessReasonCodes_DoNotInvalidateUsableRecommendation()
    {
        var result = ClassifierOutputNormalizer.NormalizePrimary("""
            {
              "recommendation":"KEEP",
              "confidence":"HIGH",
              "category":"professional",
              "reasonCodes":["relationship_signal","relationship-signal","professional_content","starred_or_important","transactional_content","security_content","receipt_or_order_content"],
              "explanation":"Relationship and protected evidence outweigh the limited recurring-mail counterevidence."
            }
            """);

        Assert.NotNull(result.Output);
        Assert.Equal(ClassifierRecommendation.Keep, result.Output.Recommendation);
        Assert.Equal(6, result.Output.ReasonCodes.Count);
        Assert.Equal(7, result.RawReasonCodes.Count);
        Assert.Contains("reason_codes_deduplicated", result.Warnings);
        Assert.Contains("reason_code_count_exceeds_five", result.Warnings);
    }

    [Fact]
    public void UnknownReasonCodes_AreRetainedDiagnosticallyAndIgnoredCanonically()
    {
        var result = ClassifierOutputNormalizer.NormalizePrimary("""
            {"recommendation":"unwanted","confidence":"medium","category":"marketing","reasonCodes":["bulk_mail","made_up_signal"],"explanation":"Promotional bulk mail dominates."}
            """);

        Assert.Equal([ClassifierReasonCode.BulkMail], result.Output!.ReasonCodes);
        Assert.Equal(["bulk_mail", "made_up_signal"], result.RawReasonCodes);
        Assert.Contains("unknown_reason_codes_retained", result.Warnings);
    }

    [Fact]
    public void MissingConfidenceAndCategory_DefaultWithoutFailure()
    {
        var result = ClassifierOutputNormalizer.NormalizePrimary("""
            {"recommendation":"needs-review","explanation":"The evidence is mixed and review is safer."}
            """);

        Assert.Equal(ClassifierRecommendation.NeedsReview, result.Output!.Recommendation);
        Assert.Equal(ClassifierConfidence.Low, result.Output.Confidence);
        Assert.Equal(ClassifierCategory.Unknown, result.Output.Category);
        Assert.Contains("confidence_defaulted", result.Warnings);
        Assert.Contains("category_defaulted", result.Warnings);
    }

    [Fact]
    public void ExplanationAndExtraProperties_AreRetainedAndTolerated()
    {
        var explanation = new string('x', 400) + "\nMaterial counterevidence remains visible.";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            recommendation = "keep",
            confidence = "low",
            category = "mixed",
            reasonCodes = Array.Empty<string>(),
            explanation,
            diagnosticExtra = new { ignored = true }
        });

        var result = ClassifierOutputNormalizer.NormalizePrimary(json);

        Assert.Equal(explanation, result.Explanation);
        Assert.Equal(explanation, result.Output!.Rationale);
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public void KeepWithOnlyNoiseSignals_ProducesNonFatalSemanticWarning()
    {
        var result = ClassifierOutputNormalizer.NormalizePrimary("""
            {"recommendation":"keep","confidence":"medium","category":"newsletter","reasonCodes":["promotional_content","bulk_mail","no_relationship"],"explanation":"The candidate nevertheless declared keep."}
            """);

        Assert.Equal(ClassifierRecommendation.Keep, result.Output!.Recommendation);
        Assert.Contains("keep_with_only_noise_signals", ClassifierOutputNormalizer.SemanticWarnings(result.Output));
    }

    [Fact]
    public void UnwantedWithOnlyProtectedSignals_ProducesNonFatalSemanticWarning()
    {
        var result = ClassifierOutputNormalizer.NormalizePrimary("""
            {"recommendation":"unwanted","confidence":"high","category":"finance","reasonCodes":["relationship_signal","financial_or_legal_content"],"explanation":"The candidate nevertheless declared unwanted."}
            """);

        Assert.Equal(ClassifierRecommendation.Unwanted, result.Output!.Recommendation);
        Assert.Contains("unwanted_with_only_protected_signals", ClassifierOutputNormalizer.SemanticWarnings(result.Output));
    }
}
