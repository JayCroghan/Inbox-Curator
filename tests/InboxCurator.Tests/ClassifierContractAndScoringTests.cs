using InboxCurator.Classification;
using InboxCurator.Data;

namespace InboxCurator.Tests;

public sealed class ClassifierContractAndScoringTests
{
    [Fact]
    public void StructuredOutput_MapsValidContractAndRejectsInvalidDomainResults()
    {
        var valid = ClassifierOutputValidator.Parse("""
            {"recommendation":"needs_review","confidence":"low","category":"mixed","reasonCodes":["mixed_content","insufficient_evidence"],"rationale":"Mixed evidence makes abstention safer."}
            """);
        Assert.Equal(ClassifierRecommendation.NeedsReview, valid.Recommendation);
        Assert.Equal(ClassifierCategory.Mixed, valid.Category);
        Assert.Equal([ClassifierReasonCode.MixedContent, ClassifierReasonCode.InsufficientEvidence], valid.ReasonCodes);

        Assert.Throws<ClassifierSchemaException>(() => ClassifierOutputValidator.Parse(
            """{"recommendation":"remove","confidence":"high","category":"mixed","reasonCodes":["mixed_content"],"rationale":"Bad enum."}"""));
        Assert.Throws<ClassifierSchemaException>(() => ClassifierOutputValidator.Parse(
            """{"recommendation":"keep","confidence":0.9,"category":"personal","reasonCodes":["relationship_signal"],"rationale":"Numeric confidence is forbidden."}"""));
        Assert.Throws<ClassifierSchemaException>(() => ClassifierOutputValidator.Parse(
            """{"recommendation":"keep","confidence":"high","category":"personal","reasonCodes":["relationship_signal"],"rationale":"Valid text.","extra":true}"""));
    }

    [Fact]
    public void Scoring_PrioritizesDangerousErrorsAbstentionAndPerformance()
    {
        var run = new ClassifierRun
        {
            Id = "score-run",
            Stage = ClassifierRunStage.Holdout,
            State = ClassifierRunState.Completed,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var profile = new ClassifierRunProfile
        {
            ClassifierRunId = run.Id,
            ProfileKey = "profile",
            ModelName = "model",
            KeepAlive = "30m",
            ModelSizeBytes = 100,
            SizeVramBytes = 80,
            PredominantlyVramResident = false,
            ColdLoadDurationNanoseconds = 40_000_000_000,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(1)
        };
        EvaluationScoredItem Item(
            EvaluationGroundTruth truth,
            ClassifierRecommendation recommendation,
            ClassifierConfidence confidence,
            bool cold = false) => new(
                truth, ClassifierResultStatus.Completed, recommendation, confidence, cold,
                2_000_000_000, 600, 100, 2_000_000_000);
        var items = new[]
        {
            Item(EvaluationGroundTruth.Keep, ClassifierRecommendation.Unwanted, ClassifierConfidence.High, cold: true),
            Item(EvaluationGroundTruth.Keep, ClassifierRecommendation.NeedsReview, ClassifierConfidence.Low),
            Item(EvaluationGroundTruth.Unwanted, ClassifierRecommendation.Unwanted, ClassifierConfidence.High),
            Item(EvaluationGroundTruth.Unwanted, ClassifierRecommendation.Keep, ClassifierConfidence.Medium),
            new EvaluationScoredItem(EvaluationGroundTruth.Unwanted, ClassifierResultStatus.SchemaFailure, null, null, false, null, null, null, null),
            new EvaluationScoredItem(EvaluationGroundTruth.Keep, ClassifierResultStatus.RequestFailure, null, null, false, null, null, null, null)
        };

        var score = EvaluationScoreCalculator.Calculate(run, profile, items);

        Assert.Equal(SafetyGateStatus.Failed, score.SafetyGate);
        Assert.Equal(1, score.KeepToUnwantedCount);
        Assert.Equal(1, score.HighConfidenceKeepToUnwantedCount);
        Assert.Equal(50, score.HighConfidenceUnwantedPrecisionPercentage);
        Assert.Equal(1, score.SchemaFailureCount);
        Assert.Equal(1, score.RequestFailureCount);
        Assert.Equal(50, score.NeedsReviewRatePercentage * 3, 5);
        Assert.Equal(50, score.MedianGenerationTokensPerSecond);
        Assert.Equal(80, score.VramPercentage);
        Assert.False(score.PredominantlyVramResident);
    }

    [Fact]
    public void DevelopmentRun_NeverClaimsTheLockedHoldoutSafetyGate()
    {
        var run = new ClassifierRun
        {
            Id = "development",
            Stage = ClassifierRunStage.DevelopmentValidation,
            State = ClassifierRunState.Completed,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var profile = new ClassifierRunProfile
        {
            ClassifierRunId = run.Id,
            ProfileKey = "p",
            ModelName = "m",
            KeepAlive = "30m"
        };
        var result = new EvaluationScoredItem(
            EvaluationGroundTruth.Keep, ClassifierResultStatus.Completed,
            ClassifierRecommendation.Unwanted, ClassifierConfidence.High,
            false, null, null, null, null);

        Assert.Equal(SafetyGateStatus.Preflight, EvaluationScoreCalculator.Calculate(run, profile, [result]).SafetyGate);
    }
}
