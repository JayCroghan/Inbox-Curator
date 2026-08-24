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
            State = ClassifierProfileState.Completed,
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

        var score = EvaluationScoreCalculator.Calculate(run, profile, items, items.Length);

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

        Assert.Equal(SafetyGateStatus.Preflight, EvaluationScoreCalculator.Calculate(run, profile, [result], 1).SafetyGate);
    }

    [Fact]
    public void PartialActiveHoldout_IsIncomplete()
    {
        Assert.Equal(SafetyGateStatus.Incomplete, Gate(
            ClassifierRunState.Running,
            ClassifierProfileState.Running,
            [Completed(EvaluationGroundTruth.Keep, ClassifierRecommendation.Keep)],
            expectedItemCount: 2));
    }

    [Fact]
    public void HoldoutWithRequestFailure_IsIncomplete()
    {
        Assert.Equal(SafetyGateStatus.Incomplete, Gate(
            ClassifierRunState.Completed,
            ClassifierProfileState.Completed,
            [
                Completed(EvaluationGroundTruth.Keep, ClassifierRecommendation.Keep),
                Failure(EvaluationGroundTruth.Unwanted, ClassifierResultStatus.RequestFailure)
            ],
            expectedItemCount: 2));
    }

    [Fact]
    public void HoldoutWithSchemaFailure_IsIncomplete()
    {
        Assert.Equal(SafetyGateStatus.Incomplete, Gate(
            ClassifierRunState.Completed,
            ClassifierProfileState.Completed,
            [Failure(EvaluationGroundTruth.Keep, ClassifierResultStatus.SchemaFailure)],
            expectedItemCount: 1));
    }

    [Fact]
    public void HoldoutWithMissingModel_IsIncomplete()
    {
        Assert.Equal(SafetyGateStatus.Incomplete, Gate(
            ClassifierRunState.Completed,
            ClassifierProfileState.Missing,
            [Failure(EvaluationGroundTruth.Keep, ClassifierResultStatus.RequestFailure)],
            expectedItemCount: 1));
    }

    [Fact]
    public void CleanFullyCompletedHoldout_Passes()
    {
        Assert.Equal(SafetyGateStatus.Passed, Gate(
            ClassifierRunState.Completed,
            ClassifierProfileState.Completed,
            [
                Completed(EvaluationGroundTruth.Keep, ClassifierRecommendation.Keep),
                Completed(EvaluationGroundTruth.Unwanted, ClassifierRecommendation.Unwanted)
            ],
            expectedItemCount: 2));
    }

    [Fact]
    public void HighConfidenceDangerousHoldout_FailsEvenBeforeCompletion()
    {
        Assert.Equal(SafetyGateStatus.Failed, Gate(
            ClassifierRunState.Running,
            ClassifierProfileState.Running,
            [Completed(EvaluationGroundTruth.Keep, ClassifierRecommendation.Unwanted, ClassifierConfidence.High)],
            expectedItemCount: 2));
    }

    [Fact]
    public void V2Scoring_ReportsCanonicalNormalizationWarningsAndRepairOverhead()
    {
        var run = new ClassifierRun
        {
            Id = "v2-score",
            Stage = ClassifierRunStage.DevelopmentValidation,
            State = ClassifierRunState.Completed,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ClassifierPromptVersion = new ClassifierPromptVersion
            {
                Version = ClassifierPromptV2Definition.Version,
                SystemPrompt = ClassifierPromptV2Definition.SystemPrompt,
                SystemPromptSha256 = ClassifierPromptV2Definition.SystemPromptSha256,
                OutputSchemaVersion = ClassifierPromptV2Definition.OutputSchemaVersion,
                OutputJsonSchema = ClassifierPromptV2Definition.OutputJsonSchema,
                ResponseProtocol = ClassifierResponseProtocol.NormalizeRepairV2,
                CreatedAtUtc = DateTime.UtcNow
            }
        };
        var profile = new ClassifierRunProfile
        {
            ClassifierRunId = run.Id,
            ProfileKey = "v2-profile",
            ModelName = "model",
            KeepAlive = "30m",
            State = ClassifierProfileState.Completed
        };
        var items = new[]
        {
            new EvaluationScoredItem(
                EvaluationGroundTruth.Keep, ClassifierResultStatus.Completed,
                ClassifierRecommendation.Keep, ClassifierConfidence.High,
                true, 10_000_000_000, 600, 80, 2_000_000_000,
                ClassifierNormalizationMode.Direct, 1, null),
            new EvaluationScoredItem(
                EvaluationGroundTruth.Unwanted, ClassifierResultStatus.Completed,
                ClassifierRecommendation.Unwanted, ClassifierConfidence.High,
                false, 3_000_000_000, 600, 80, 2_000_000_000,
                ClassifierNormalizationMode.SelfRepaired, 1, 1_000_000_000),
            new EvaluationScoredItem(
                EvaluationGroundTruth.Keep, ClassifierResultStatus.SchemaFailure,
                null, null, false, 4_000_000_000, 600, 80, 2_000_000_000,
                ClassifierNormalizationMode.Failed, 0, 3_000_000_000)
        };

        var score = EvaluationScoreCalculator.Calculate(run, profile, items, expectedItemCount: 3);

        Assert.Equal(ClassifierPromptV2Definition.Version, score.PromptVersion);
        Assert.Equal(ClassifierResponseProtocol.NormalizeRepairV2, score.ResponseProtocol);
        Assert.Equal(2, score.ValidCanonicalResultCount);
        Assert.Equal(1, score.DirectNormalizationCount);
        Assert.Equal(100d / 3, score.DirectNormalizationPercentage, 5);
        Assert.Equal(1, score.SelfRepairedCount);
        Assert.Equal(1, score.UnresolvedNormalizationFailureCount);
        Assert.Equal(2, score.SemanticWarningCount);
        Assert.Equal(3_500, score.MedianSteadyStateDurationMilliseconds);
        Assert.Equal(2_000, score.MedianRepairDurationMilliseconds);
        Assert.Equal(4, score.TotalRepairOverheadSeconds);
    }

    private static SafetyGateStatus Gate(
        ClassifierRunState runState,
        ClassifierProfileState profileState,
        IReadOnlyCollection<EvaluationScoredItem> items,
        int expectedItemCount)
    {
        var run = new ClassifierRun
        {
            Id = "gate-run",
            Stage = ClassifierRunStage.Holdout,
            State = runState,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var profile = new ClassifierRunProfile
        {
            ClassifierRunId = run.Id,
            ProfileKey = "gate-profile",
            ModelName = "model",
            KeepAlive = "30m",
            State = profileState
        };
        return EvaluationScoreCalculator.Calculate(run, profile, items, expectedItemCount).SafetyGate;
    }

    private static EvaluationScoredItem Completed(
        EvaluationGroundTruth truth,
        ClassifierRecommendation recommendation,
        ClassifierConfidence confidence = ClassifierConfidence.Medium) =>
        new(truth, ClassifierResultStatus.Completed, recommendation, confidence, false, null, null, null, null);

    private static EvaluationScoredItem Failure(EvaluationGroundTruth truth, ClassifierResultStatus status) =>
        new(truth, status, null, null, false, null, null, null, null);
}
