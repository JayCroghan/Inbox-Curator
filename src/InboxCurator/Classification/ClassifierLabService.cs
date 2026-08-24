using System.Text.Json;
using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InboxCurator.Classification;

public enum ClassifierInspectionMode
{
    Disagreements,
    SelfRepaired,
    SemanticWarnings,
    NormalizationWarnings,
    All
}

public sealed record CorpusLabSummary(
    long Id,
    string Version,
    DateTime CreatedAtUtc,
    int EligibleSourceCount,
    int KeepCount,
    int UnwantedCount,
    int DevelopmentCount,
    int ValidationCount,
    int HoldoutCount,
    int ExcludedCleanExistingOnlyCount,
    int ExcludedCleanOlderThanCount,
    int ExcludedDeferCount,
    int ExcludedMissingEvidenceCount,
    string SplitStrategy);

public sealed record PromptLabSummary(
    string Version,
    string Hash,
    string SchemaVersion,
    string? SchemaHash,
    ClassifierResponseProtocol ResponseProtocol,
    string? RepairPromptVersion,
    string? RepairPromptHash,
    string? RepairSchemaVersion,
    string? RepairSchemaHash,
    bool IsLocked,
    DateTime? LockedAtUtc,
    long? LockedEvaluationCorpusId,
    string? LockedEvaluationCorpusVersion)
{
    public bool IsHoldoutReady => IsLocked && LockedEvaluationCorpusId.HasValue;
}

public sealed record ModelProfileLabSummary(
    string Key,
    string Model,
    string Think,
    double Temperature,
    int ContextLength,
    string KeepAlive,
    bool? IsInstalled);

public sealed record RunLabSummary(
    string Id,
    long EvaluationCorpusId,
    string CorpusVersion,
    string PromptVersion,
    ClassifierResponseProtocol ResponseProtocol,
    ClassifierRunStage Stage,
    ClassifierRunState State,
    int CompletedItems,
    int FailedItems,
    int TotalItems,
    string? CurrentProfileKey,
    EvaluationSplit? CurrentSplit,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? FailureCode)
{
    public bool IsActive => State is ClassifierRunState.Queued or ClassifierRunState.Running or ClassifierRunState.CancelRequested;
    public int ProcessedItems => CompletedItems + FailedItems;
    public double ProgressPercentage => TotalItems == 0 ? 0 : Math.Clamp(ProcessedItems * 100d / TotalItems, 0, 100);
}

public sealed record ClassifierDisagreement(
    long ResultId,
    string ProfileKey,
    string ModelName,
    ClassifierResultStatus Status,
    string? FailureCode,
    ClusterTargetType TargetType,
    string TargetValue,
    string DisplayName,
    EvaluationGroundTruth GroundTruth,
    ClassifierRecommendation? Recommendation,
    ClassifierConfidence? Confidence,
    ClassifierCategory? Category,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> RawReasonCodes,
    string? Rationale,
    string? PrimaryResponse,
    string? RepairResponse,
    ClassifierNormalizationMode? NormalizationMode,
    IReadOnlyList<string> NormalizationWarnings,
    IReadOnlyList<string> SemanticWarnings,
    string? RepairFailureCode,
    int MessageCount,
    DateTime FirstReceivedUtc,
    DateTime LastReceivedUtc,
    int UnreadCount,
    int StarredCount,
    int ImportantCount,
    int RelationshipCount,
    double PromotionsPercentage,
    double ListUnsubscribePercentage,
    double AttachmentPercentage,
    IReadOnlyList<string> RepresentativeSubjects);

public sealed record ClassifierLabSnapshot(
    CorpusLabSummary? Corpus,
    PromptLabSummary Prompt,
    IReadOnlyList<CorpusLabSummary> Corpora,
    IReadOnlyList<PromptLabSummary> Prompts,
    IReadOnlyList<ModelProfileLabSummary> Profiles,
    bool OllamaReachable,
    RunLabSummary? SelectedRun,
    IReadOnlyList<RunLabSummary> RecentRuns,
    IReadOnlyList<ClassifierScore> Scores,
    IReadOnlyList<ClassifierDisagreement> InspectionResults,
    int InspectionResultCount,
    ClassifierInspectionMode InspectionMode,
    int ErrorPage,
    int ErrorPageCount,
    bool HoldoutDetailsVisible);

public sealed class ClassifierLabService(
    IDbContextFactory<InboxCuratorDbContext> dbFactory,
    ILocalModelRuntime runtime,
    IOptions<OllamaOptions> options)
{
    private const int DisagreementPageSize = 20;

    public async Task<ClassifierLabSnapshot> GetAsync(
        string? runId,
        long? profileId,
        string scoreSort,
        string direction,
        ClassifierInspectionMode inspectionMode,
        int errorPage,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var corpusEntities = await db.EvaluationCorpora.AsNoTracking()
            .Include(item => item.Items)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        var corpus = corpusEntities.FirstOrDefault();
        var promptEntities = await db.ClassifierPromptVersions.AsNoTracking()
            .Include(item => item.LockedEvaluationCorpus)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        var prompt = promptEntities.Single(item => item.Version == ClassifierPromptV2Definition.Version);

        IReadOnlySet<string>? installed = null;
        try
        {
            installed = await runtime.GetInstalledModelsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }

        var profiles = options.Value.Profiles.Select(item => new ModelProfileLabSummary(
            item.Key,
            item.Model,
            item.Think ?? "default",
            item.Temperature,
            item.NumCtx,
            item.KeepAlive,
            installed?.Contains(item.Model))).ToArray();
        var recentRuns = await db.ClassifierRuns.AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(12)
            .Select(item => new RunLabSummary(
                item.Id,
                item.EvaluationCorpusId,
                item.EvaluationCorpus.Version,
                item.ClassifierPromptVersion.Version,
                item.ClassifierPromptVersion.ResponseProtocol,
                item.Stage,
                item.State,
                item.CompletedItems,
                item.FailedItems,
                item.TotalItems,
                item.CurrentProfileKey,
                item.CurrentSplit,
                item.CreatedAtUtc,
                item.StartedAtUtc,
                item.CompletedAtUtc,
                item.FailureCode))
            .ToArrayAsync(cancellationToken);
        var selectedRunSummary = recentRuns.FirstOrDefault(item => item.Id == runId) ?? recentRuns.FirstOrDefault();
        var scores = Array.Empty<ClassifierScore>();
        var inspectionResults = Array.Empty<ClassifierDisagreement>();
        var inspectionResultCount = 0;
        var pageCount = 0;
        var holdoutDetailsVisible = selectedRunSummary?.Stage != ClassifierRunStage.Holdout;

        if (selectedRunSummary is not null)
        {
            var run = await db.ClassifierRuns.AsNoTracking()
                .Include(item => item.Profiles)
                .Include(item => item.ClassifierPromptVersion)
                .SingleAsync(item => item.Id == selectedRunSummary.Id, cancellationToken);
            var scoredRows = await db.ClassifierResults.AsNoTracking()
                .Where(item => item.ClassifierRunId == run.Id)
                .Select(item => new
                {
                    item.ClassifierRunProfileId,
                    item.EvaluationCorpusItem.GroundTruth,
                    item.Status,
                    item.Recommendation,
                    item.Confidence,
                    item.IsColdLoadRequest,
                    item.TotalDurationNanoseconds,
                    item.PromptEvalCount,
                    item.EvalCount,
                    item.EvalDurationNanoseconds,
                    item.NormalizationMode,
                    item.SemanticWarningsJson,
                    item.RepairTotalDurationNanoseconds
                })
                .ToArrayAsync(cancellationToken);
            var expectedItemCount = await db.EvaluationCorpusItems.AsNoTracking().CountAsync(
                item => item.EvaluationCorpusId == run.EvaluationCorpusId &&
                    (run.Stage == ClassifierRunStage.Holdout
                        ? item.Split == EvaluationSplit.Holdout
                        : item.Split != EvaluationSplit.Holdout),
                cancellationToken);
            scores = SortScores(run.Profiles
                .OrderBy(item => item.ExecutionOrder)
                .Select(profile => EvaluationScoreCalculator.Calculate(
                    run,
                    profile,
                    scoredRows.Where(item => item.ClassifierRunProfileId == profile.Id)
                        .Select(item => new EvaluationScoredItem(
                            item.GroundTruth,
                            item.Status,
                            item.Recommendation,
                            item.Confidence,
                            item.IsColdLoadRequest,
                            item.TotalDurationNanoseconds,
                            item.PromptEvalCount,
                            item.EvalCount,
                            item.EvalDurationNanoseconds,
                            item.NormalizationMode,
                            DeserializeStrings(item.SemanticWarningsJson).Length,
                            item.RepairTotalDurationNanoseconds))
                        .ToArray(),
                    expectedItemCount))
                .ToArray(), scoreSort, direction);

            if (holdoutDetailsVisible)
            {
                var query = db.ClassifierResults.AsNoTracking()
                    .Where(item => item.ClassifierRunId == run.Id);
                query = inspectionMode switch
                {
                    ClassifierInspectionMode.SelfRepaired => query.Where(item =>
                        item.NormalizationMode == ClassifierNormalizationMode.SelfRepaired),
                    ClassifierInspectionMode.SemanticWarnings => query.Where(item =>
                        item.SemanticWarningsJson != null && item.SemanticWarningsJson != "[]"),
                    ClassifierInspectionMode.NormalizationWarnings => query.Where(item =>
                        item.NormalizationWarningsJson != null && item.NormalizationWarningsJson != "[]"),
                    ClassifierInspectionMode.All => query,
                    _ => query.Where(item =>
                        item.Status != ClassifierResultStatus.Completed ||
                        (item.EvaluationCorpusItem.GroundTruth == EvaluationGroundTruth.Keep && item.Recommendation != ClassifierRecommendation.Keep) ||
                        (item.EvaluationCorpusItem.GroundTruth == EvaluationGroundTruth.Unwanted && item.Recommendation != ClassifierRecommendation.Unwanted))
                };
                if (profileId.HasValue)
                {
                    query = query.Where(item => item.ClassifierRunProfileId == profileId.Value);
                }

                inspectionResultCount = await query.CountAsync(cancellationToken);
                pageCount = Math.Max(1, (int)Math.Ceiling(inspectionResultCount / (double)DisagreementPageSize));
                errorPage = Math.Clamp(errorPage, 1, pageCount);
                var rows = await query
                    .OrderByDescending(item => item.EvaluationCorpusItem.GroundTruth == EvaluationGroundTruth.Keep && item.Recommendation == ClassifierRecommendation.Unwanted)
                    .ThenBy(item => item.ClassifierRunProfile.ExecutionOrder)
                    .ThenBy(item => item.EvaluationCorpusItem.DisplayName)
                    .Skip((errorPage - 1) * DisagreementPageSize)
                    .Take(DisagreementPageSize)
                    .Select(item => new
                    {
                        item.Id,
                        item.ClassifierRunProfile.ProfileKey,
                        item.ClassifierRunProfile.ModelName,
                        item.Status,
                        item.FailureCode,
                        Corpus = item.EvaluationCorpusItem,
                        item.Recommendation,
                        item.Confidence,
                        item.Category,
                        item.ReasonCodesJson,
                        item.RawReasonCodesJson,
                        item.Rationale,
                        item.PrimaryExplanation,
                        item.PrimaryResponse,
                        item.RepairResponse,
                        item.NormalizationMode,
                        item.NormalizationWarningsJson,
                        item.SemanticWarningsJson,
                        item.RepairFailureCode
                    })
                    .ToArrayAsync(cancellationToken);
                inspectionResults = rows.Select(item => new ClassifierDisagreement(
                    item.Id,
                    item.ProfileKey,
                    item.ModelName,
                    item.Status,
                    item.FailureCode,
                    item.Corpus.TargetType,
                    item.Corpus.TargetValue,
                    item.Corpus.DisplayName,
                    item.Corpus.GroundTruth,
                    item.Recommendation,
                    item.Confidence,
                    item.Category,
                    DeserializeStrings(item.ReasonCodesJson),
                    DeserializeStrings(item.RawReasonCodesJson),
                    item.PrimaryExplanation ?? item.Rationale,
                    item.PrimaryResponse,
                    item.RepairResponse,
                    item.NormalizationMode,
                    DeserializeStrings(item.NormalizationWarningsJson),
                    DeserializeStrings(item.SemanticWarningsJson),
                    item.RepairFailureCode,
                    item.Corpus.MessageCount,
                    item.Corpus.FirstReceivedUtc,
                    item.Corpus.LastReceivedUtc,
                    item.Corpus.UnreadCount,
                    item.Corpus.StarredCount,
                    item.Corpus.ImportantCount,
                    item.Corpus.RelationshipCount,
                    Percentage(item.Corpus.PromotionsCount, item.Corpus.MessageCount),
                    Percentage(item.Corpus.ListUnsubscribeCount, item.Corpus.MessageCount),
                    Percentage(item.Corpus.AttachmentCount, item.Corpus.MessageCount),
                    DeserializeStrings(item.Corpus.RepresentativeSubjectsJson))).ToArray();
            }
        }

        var corpusSummaries = corpusEntities.Select(MapCorpus).ToArray();
        var promptSummaries = promptEntities.Select(MapPrompt).ToArray();
        return new ClassifierLabSnapshot(
            corpus is null ? null : MapCorpus(corpus),
            MapPrompt(prompt),
            corpusSummaries,
            promptSummaries,
            profiles,
            installed is not null,
            selectedRunSummary,
            recentRuns,
            scores,
            inspectionResults,
            inspectionResultCount,
            inspectionMode,
            errorPage,
            pageCount,
            holdoutDetailsVisible);
    }

    private static CorpusLabSummary MapCorpus(EvaluationCorpus corpus) => new(
        corpus.Id,
        corpus.Version,
        corpus.CreatedAtUtc,
        corpus.EligibleSourceCount,
        corpus.Items.Count(item => item.GroundTruth == EvaluationGroundTruth.Keep),
        corpus.Items.Count(item => item.GroundTruth == EvaluationGroundTruth.Unwanted),
        corpus.Items.Count(item => item.Split == EvaluationSplit.Development),
        corpus.Items.Count(item => item.Split == EvaluationSplit.Validation),
        corpus.Items.Count(item => item.Split == EvaluationSplit.Holdout),
        corpus.ExcludedCleanExistingOnlyCount,
        corpus.ExcludedCleanOlderThanCount,
        corpus.ExcludedDeferCount,
        corpus.ExcludedMissingEvidenceCount,
        corpus.SplitStrategy);

    private static PromptLabSummary MapPrompt(ClassifierPromptVersion prompt) => new(
        prompt.Version,
        prompt.SystemPromptSha256,
        prompt.OutputSchemaVersion,
        prompt.OutputJsonSchemaSha256,
        prompt.ResponseProtocol,
        prompt.RepairPromptVersion,
        prompt.RepairSystemPromptSha256,
        prompt.RepairOutputSchemaVersion,
        prompt.RepairOutputJsonSchemaSha256,
        prompt.IsLocked,
        prompt.LockedAtUtc,
        prompt.LockedEvaluationCorpusId,
        prompt.LockedEvaluationCorpus?.Version);

    private static ClassifierScore[] SortScores(ClassifierScore[] scores, string sort, string direction)
    {
        Func<ClassifierScore, object?> selector = sort.ToLowerInvariant() switch
        {
            "dangerous" => item => item.KeepToUnwantedCount,
            "hcdangerous" => item => item.HighConfidenceKeepToUnwantedCount,
            "precision" => item => item.HighConfidenceUnwantedPrecisionPercentage,
            "review" => item => item.NeedsReviewRatePercentage,
            "decisive" => item => item.OverallDecisiveCoveragePercentage,
            "accuracy" => item => item.ExactBinaryAccuracyPercentage,
            "latency" => item => item.MedianSteadyStateDurationMilliseconds,
            "tokens" => item => item.MedianGenerationTokensPerSecond,
            "cold" => item => item.ColdLoadDurationSeconds,
            "vram" => item => item.VramPercentage,
            _ => item => item.ProfileKey
        };
        return string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase)
            ? scores.OrderBy(selector).ToArray()
            : scores.OrderByDescending(selector).ToArray();
    }

    private static string[] DeserializeStrings(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static double Percentage(int value, int total) => total == 0 ? 0 : value * 100d / total;
}
