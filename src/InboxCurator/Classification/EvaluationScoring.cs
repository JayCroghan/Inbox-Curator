using InboxCurator.Data;

namespace InboxCurator.Classification;

public enum SafetyGateStatus
{
    Preflight,
    Incomplete,
    Passed,
    Failed
}

public sealed record EvaluationScoredItem(
    EvaluationGroundTruth GroundTruth,
    ClassifierResultStatus Status,
    ClassifierRecommendation? Recommendation,
    ClassifierConfidence? Confidence,
    bool IsColdLoadRequest,
    long? TotalDurationNanoseconds,
    int? PromptEvalCount,
    int? EvalCount,
    long? EvalDurationNanoseconds,
    ClassifierNormalizationMode? NormalizationMode = null,
    int SemanticWarningCount = 0,
    long? RepairTotalDurationNanoseconds = null);

public sealed record ClassifierScore(
    string RunId,
    long RunProfileId,
    string ProfileKey,
    string ModelName,
    ClassifierRunStage Stage,
    SafetyGateStatus SafetyGate,
    int GroundTruthKeepCount,
    int GroundTruthUnwantedCount,
    int OutputKeepCount,
    int OutputUnwantedCount,
    int OutputNeedsReviewCount,
    int KeepToUnwantedCount,
    int KeepToNeedsReviewCount,
    int UnwantedToKeepCount,
    int UnwantedToNeedsReviewCount,
    int CorrectDecisiveCount,
    int HighConfidenceKeepToUnwantedCount,
    double? HighConfidenceUnwantedPrecisionPercentage,
    double HighConfidenceDecisiveCoveragePercentage,
    double? ExactBinaryAccuracyPercentage,
    double OverallDecisiveCoveragePercentage,
    double NeedsReviewRatePercentage,
    int SchemaFailureCount,
    int RequestFailureCount,
    double? MedianSteadyStateDurationMilliseconds,
    double? P95SteadyStateDurationMilliseconds,
    double? MedianPromptTokens,
    double? MedianOutputTokens,
    double? MedianGenerationTokensPerSecond,
    double? ColdLoadDurationSeconds,
    double? TotalProfileDurationSeconds,
    double? VramPercentage,
    bool? PredominantlyVramResident,
    string PromptVersion,
    ClassifierResponseProtocol ResponseProtocol,
    int ValidCanonicalResultCount,
    int ExpectedResultCount,
    int DirectNormalizationCount,
    double DirectNormalizationPercentage,
    int SelfRepairedCount,
    double SelfRepairedPercentage,
    int UnresolvedNormalizationFailureCount,
    int SemanticWarningCount,
    double? MedianRepairDurationMilliseconds,
    double TotalRepairOverheadSeconds);

public static class EvaluationScoreCalculator
{
    public static ClassifierScore Calculate(
        ClassifierRun run,
        ClassifierRunProfile profile,
        IReadOnlyCollection<EvaluationScoredItem> items,
        int expectedItemCount)
    {
        var completed = items.Where(item => item.Status == ClassifierResultStatus.Completed).ToArray();
        var decisive = completed.Where(item => item.Recommendation is
            ClassifierRecommendation.Keep or ClassifierRecommendation.Unwanted).ToArray();
        var highConfidenceUnwanted = completed.Where(item =>
            item.Recommendation == ClassifierRecommendation.Unwanted &&
            item.Confidence == ClassifierConfidence.High).ToArray();
        var dangerous = completed.Count(item =>
            item.GroundTruth == EvaluationGroundTruth.Keep &&
            item.Recommendation == ClassifierRecommendation.Unwanted);
        var highConfidenceDangerous = completed.Count(item =>
            item.GroundTruth == EvaluationGroundTruth.Keep &&
            item.Recommendation == ClassifierRecommendation.Unwanted &&
            item.Confidence == ClassifierConfidence.High);
        var correctDecisive = decisive.Count(item =>
            (item.GroundTruth == EvaluationGroundTruth.Keep && item.Recommendation == ClassifierRecommendation.Keep) ||
            (item.GroundTruth == EvaluationGroundTruth.Unwanted && item.Recommendation == ClassifierRecommendation.Unwanted));
        var highConfidenceDecisive = decisive.Count(item => item.Confidence == ClassifierConfidence.High);
        var steady = items.Where(item => !item.IsColdLoadRequest).ToArray();
        var steadyDurations = steady
            .Where(item => item.TotalDurationNanoseconds.HasValue)
            .Select(item => item.TotalDurationNanoseconds!.Value / 1_000_000d)
            .OrderBy(value => value)
            .ToArray();
        var generationRates = steady
            .Where(item => item.EvalCount.HasValue && item.EvalDurationNanoseconds > 0)
            .Select(item => item.EvalCount!.Value / (item.EvalDurationNanoseconds!.Value / 1_000_000_000d))
            .OrderBy(value => value)
            .ToArray();
        var repairDurations = items
            .Where(item => item.RepairTotalDurationNanoseconds.HasValue)
            .Select(item => item.RepairTotalDurationNanoseconds!.Value / 1_000_000d)
            .OrderBy(value => value)
            .ToArray();
        var total = items.Count;
        var vramPercentage = profile.ModelSizeBytes > 0 && profile.SizeVramBytes.HasValue
            ? Math.Clamp(profile.SizeVramBytes.Value * 100d / profile.ModelSizeBytes.Value, 0, 100)
            : (double?)null;
        var safetyGate = SafetyGate(
            run,
            profile,
            items,
            completed.Length,
            expectedItemCount,
            highConfidenceDangerous);

        return new ClassifierScore(
            run.Id,
            profile.Id,
            profile.ProfileKey,
            profile.ModelName,
            run.Stage,
            safetyGate,
            items.Count(item => item.GroundTruth == EvaluationGroundTruth.Keep),
            items.Count(item => item.GroundTruth == EvaluationGroundTruth.Unwanted),
            completed.Count(item => item.Recommendation == ClassifierRecommendation.Keep),
            completed.Count(item => item.Recommendation == ClassifierRecommendation.Unwanted),
            completed.Count(item => item.Recommendation == ClassifierRecommendation.NeedsReview),
            dangerous,
            completed.Count(item => item.GroundTruth == EvaluationGroundTruth.Keep && item.Recommendation == ClassifierRecommendation.NeedsReview),
            completed.Count(item => item.GroundTruth == EvaluationGroundTruth.Unwanted && item.Recommendation == ClassifierRecommendation.Keep),
            completed.Count(item => item.GroundTruth == EvaluationGroundTruth.Unwanted && item.Recommendation == ClassifierRecommendation.NeedsReview),
            correctDecisive,
            highConfidenceDangerous,
            Percentage(
                highConfidenceUnwanted.Count(item => item.GroundTruth == EvaluationGroundTruth.Unwanted),
                highConfidenceUnwanted.Length),
            PercentageOrZero(highConfidenceDecisive, total),
            Percentage(correctDecisive, decisive.Length),
            PercentageOrZero(decisive.Length, total),
            PercentageOrZero(completed.Count(item => item.Recommendation == ClassifierRecommendation.NeedsReview), total),
            items.Count(item => item.Status == ClassifierResultStatus.SchemaFailure),
            items.Count(item => item.Status == ClassifierResultStatus.RequestFailure),
            Median(steadyDurations),
            Percentile95(steadyDurations),
            Median(steady.Where(item => item.PromptEvalCount.HasValue).Select(item => (double)item.PromptEvalCount!.Value).OrderBy(value => value).ToArray()),
            Median(steady.Where(item => item.EvalCount.HasValue).Select(item => (double)item.EvalCount!.Value).OrderBy(value => value).ToArray()),
            Median(generationRates),
            profile.ColdLoadDurationNanoseconds / 1_000_000_000d,
            profile.StartedAtUtc.HasValue && profile.CompletedAtUtc.HasValue
                ? (profile.CompletedAtUtc.Value - profile.StartedAtUtc.Value).TotalSeconds
                : null,
            vramPercentage,
            profile.PredominantlyVramResident,
            run.ClassifierPromptVersion?.Version ?? "unknown",
            run.ClassifierPromptVersion?.ResponseProtocol ?? ClassifierResponseProtocol.StrictV1,
            completed.Length,
            expectedItemCount,
            items.Count(item => item.Status == ClassifierResultStatus.Completed && item.NormalizationMode == ClassifierNormalizationMode.Direct),
            PercentageOrZero(
                items.Count(item => item.Status == ClassifierResultStatus.Completed && item.NormalizationMode == ClassifierNormalizationMode.Direct),
                expectedItemCount),
            items.Count(item => item.Status == ClassifierResultStatus.Completed && item.NormalizationMode == ClassifierNormalizationMode.SelfRepaired),
            PercentageOrZero(
                items.Count(item => item.Status == ClassifierResultStatus.Completed && item.NormalizationMode == ClassifierNormalizationMode.SelfRepaired),
                expectedItemCount),
            items.Count(item => item.Status == ClassifierResultStatus.SchemaFailure),
            items.Sum(item => item.SemanticWarningCount),
            Median(repairDurations),
            items.Where(item => item.RepairTotalDurationNanoseconds.HasValue)
                .Sum(item => item.RepairTotalDurationNanoseconds!.Value) / 1_000_000_000d);
    }

    private static SafetyGateStatus SafetyGate(
        ClassifierRun run,
        ClassifierRunProfile profile,
        IReadOnlyCollection<EvaluationScoredItem> items,
        int completedCount,
        int expectedItemCount,
        int highConfidenceDangerous)
    {
        if (run.Stage != ClassifierRunStage.Holdout)
        {
            return SafetyGateStatus.Preflight;
        }

        if (highConfidenceDangerous > 0)
        {
            return SafetyGateStatus.Failed;
        }

        var completeAndValid = run.State == ClassifierRunState.Completed &&
            profile.State == ClassifierProfileState.Completed &&
            expectedItemCount > 0 &&
            items.Count == expectedItemCount &&
            completedCount == expectedItemCount;
        return completeAndValid ? SafetyGateStatus.Passed : SafetyGateStatus.Incomplete;
    }

    private static double? Percentage(int numerator, int denominator) =>
        denominator == 0 ? null : numerator * 100d / denominator;

    private static double PercentageOrZero(int numerator, int denominator) =>
        denominator == 0 ? 0 : numerator * 100d / denominator;

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var midpoint = values.Count / 2;
        return values.Count % 2 == 0 ? (values[midpoint - 1] + values[midpoint]) / 2 : values[midpoint];
    }

    private static double? Percentile95(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var index = Math.Max(0, (int)Math.Ceiling(values.Count * .95) - 1);
        return values[index];
    }
}
