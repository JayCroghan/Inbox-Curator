using System.Text.Json;
using InboxCurator.Data;
using InboxCurator.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InboxCurator.Classification;

public sealed class SyntheticEvaluationSeeder(
    IDbContextFactory<InboxCuratorDbContext> dbFactory,
    ClusterDecisionService decisions,
    EvaluationCorpusService corpora,
    ClassifierPromptService prompts,
    IOptions<OllamaOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            if (await db.EvaluationCorpora.AnyAsync(cancellationToken))
            {
                return;
            }
        }

        await decisions.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId, "weekly.buildlog.example", ClusterDecisionKind.UnwantedExistingAndFuture), cancellationToken);
        await decisions.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.Sender, "mina.chen@example.test", ClusterDecisionKind.KeepProtect), cancellationToken);
        await decisions.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId, "members.garden.example", ClusterDecisionKind.KeepProtect), cancellationToken);
        await decisions.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId, "daily.ledger.example", ClusterDecisionKind.UnwantedExistingAndFuture), cancellationToken);

        var corpus = await corpora.CreateAsync(cancellationToken);
        await prompts.EnsureAllAsync(cancellationToken);
        await SeedRunAsync(corpus.Id, ClassifierPromptDefinition.Version, new DateTime(2026, 8, 23, 7, 20, 0, DateTimeKind.Utc), cancellationToken);
        await SeedRunAsync(corpus.Id, ClassifierPromptV2Definition.Version, new DateTime(2026, 8, 23, 8, 5, 0, DateTimeKind.Utc), cancellationToken);
    }

    private async Task SeedRunAsync(
        long corpusId,
        string promptVersion,
        DateTime started,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var prompt = await db.ClassifierPromptVersions.SingleAsync(
            item => item.Version == promptVersion, cancellationToken);
        var itemsQuery = db.EvaluationCorpusItems.Where(item => item.EvaluationCorpusId == corpusId);
        itemsQuery = itemsQuery.Where(item => item.Split != EvaluationSplit.Holdout);
        var items = await itemsQuery.OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var isV2 = prompt.ResponseProtocol == ClassifierResponseProtocol.NormalizeRepairV2;
        var run = new ClassifierRun
        {
            Id = isV2 ? "synthetic-v2-development" : "synthetic-v1-development",
            EvaluationCorpusId = corpusId,
            ClassifierPromptVersionId = prompt.Id,
            Stage = ClassifierRunStage.DevelopmentValidation,
            State = ClassifierRunState.Completed,
            TotalItems = items.Length * options.Value.Profiles.Count,
            CompletedItems = items.Length * options.Value.Profiles.Count,
            CreatedAtUtc = started.AddMinutes(-1),
            StartedAtUtc = started,
            UpdatedAtUtc = started.AddMinutes(18),
            CompletedAtUtc = started.AddMinutes(18)
        };
        db.ClassifierRuns.Add(run);

        for (var profileIndex = 0; profileIndex < options.Value.Profiles.Count; profileIndex++)
        {
            var configured = options.Value.Profiles[profileIndex];
            var profile = new ClassifierRunProfile
            {
                ClassifierRunId = run.Id,
                ProfileKey = configured.Key,
                ModelName = configured.Model,
                ThinkMode = configured.Think,
                Temperature = configured.Temperature,
                ContextLength = configured.NumCtx,
                Stream = configured.Stream,
                KeepAlive = configured.KeepAlive,
                ExecutionOrder = profileIndex,
                State = ClassifierProfileState.Completed,
                StartedAtUtc = started.AddMinutes(profileIndex * 3),
                CompletedAtUtc = started.AddMinutes(profileIndex * 3 + 3),
                ColdLoadDurationNanoseconds = (42L + profileIndex * 9) * 1_000_000_000,
                ModelSizeBytes = 22_000_000_000 + profileIndex * 500_000_000L,
                SizeVramBytes = profileIndex == 2 ? 18_000_000_000 : 21_500_000_000 + profileIndex * 400_000_000L,
                RuntimeContextLength = 8192,
                PredominantlyVramResident = profileIndex != 2
            };
            run.Profiles.Add(profile);
            for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                var item = items[itemIndex];
                var recommendation = Recommendation(profileIndex, itemIndex, item.GroundTruth, ClassifierRunStage.DevelopmentValidation);
                var confidence = recommendation == ClassifierRecommendation.NeedsReview
                    ? ClassifierConfidence.Low
                    : profileIndex == 2 ? ClassifierConfidence.High : ClassifierConfidence.Medium;
                profile.Results.Add(new ClassifierResult
                {
                    ClassifierRunId = run.Id,
                    EvaluationCorpusItemId = item.Id,
                    Status = ClassifierResultStatus.Completed,
                    Recommendation = recommendation,
                    Confidence = confidence,
                    Category = recommendation switch
                    {
                        ClassifierRecommendation.Keep => ClassifierCategory.Professional,
                        ClassifierRecommendation.Unwanted => ClassifierCategory.Marketing,
                        _ => ClassifierCategory.Mixed
                    },
                    ReasonCodesJson = JsonSerializer.Serialize(recommendation switch
                    {
                        ClassifierRecommendation.Keep => new[] { "relationship_signal", "professional_content" },
                        ClassifierRecommendation.Unwanted => new[] { "promotional_content", "bulk_mail" },
                        _ => new[] { "mixed_content", "insufficient_evidence" }
                    }),
                    Rationale = recommendation == ClassifierRecommendation.NeedsReview
                        ? "The frozen evidence mixes useful and recurring bulk-mail signals, so review is safer."
                        : recommendation == ClassifierRecommendation.Unwanted
                            ? "The source is dominated by recurring promotional or bulk-mail evidence."
                            : "The source shows useful correspondence or retained transactional evidence.",
                    PrimaryResponse = isV2 ? JsonSerializer.Serialize(new
                    {
                        recommendation = itemIndex == 1 ? "candidate_declared_noncanonical" : recommendation.ToString(),
                        confidence = confidence.ToString(),
                        category = recommendation == ClassifierRecommendation.Unwanted ? "marketing" : "professional",
                        reasonCodes = recommendation == ClassifierRecommendation.Unwanted
                            ? new[] { "promotional_content", "bulk_mail" }
                            : new[] { "relationship_signal", "professional_content" },
                        explanation = recommendation == ClassifierRecommendation.Unwanted
                            ? "Recurring promotional evidence dominates, with no material protected counterevidence."
                            : "Useful correspondence evidence outweighs recurring-noise signals and supports retention."
                    }) : null,
                    RepairResponse = isV2 && itemIndex == 1
                        ? JsonSerializer.Serialize(new { recommendation = recommendation.ToString() })
                        : null,
                    PrimaryExplanation = isV2
                        ? recommendation == ClassifierRecommendation.Unwanted
                            ? "Recurring promotional evidence dominates, with no material protected counterevidence."
                            : "Useful correspondence evidence outweighs recurring-noise signals and supports retention."
                        : null,
                    RawReasonCodesJson = isV2
                        ? JsonSerializer.Serialize(recommendation == ClassifierRecommendation.Unwanted
                            ? new[] { "promotional_content", "bulk_mail" }
                            : new[] { "relationship_signal", "professional_content" })
                        : null,
                    NormalizationMode = isV2
                        ? itemIndex == 1 ? ClassifierNormalizationMode.SelfRepaired : ClassifierNormalizationMode.Direct
                        : null,
                    NormalizationWarningsJson = isV2 && itemIndex == 1
                        ? "[\"recommendation_unrecognized\"]"
                        : "[]",
                    SemanticWarningsJson = "[]",
                    ThinkingPresent = configured.Think is "true" or "low",
                    ThinkingCharacterCount = configured.Think is "true" or "low" ? 814 + itemIndex * 31 : 0,
                    IsColdLoadRequest = itemIndex == 0,
                    TotalDurationNanoseconds = (8L + profileIndex * 2 + itemIndex) * 1_000_000_000,
                    LoadDurationNanoseconds = itemIndex == 0 ? profile.ColdLoadDurationNanoseconds : 20_000_000,
                    PromptEvalCount = 620 + itemIndex * 18,
                    PromptEvalDurationNanoseconds = 850_000_000 + itemIndex * 30_000_000,
                    EvalCount = 72 + itemIndex * 4,
                    EvalDurationNanoseconds = 2_000_000_000L + profileIndex * 270_000_000L,
                    RepairTotalDurationNanoseconds = isV2 && itemIndex == 1 ? 1_400_000_000 : null,
                    RepairPromptEvalCount = isV2 && itemIndex == 1 ? 180 : null,
                    RepairEvalCount = isV2 && itemIndex == 1 ? 24 : null,
                    RepairEvalDurationNanoseconds = isV2 && itemIndex == 1 ? 600_000_000 : null,
                    StartedAtUtc = started.AddSeconds(profileIndex * 120 + itemIndex * 12),
                    CompletedAtUtc = started.AddSeconds(profileIndex * 120 + itemIndex * 12 + 9)
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ClassifierRecommendation Recommendation(
        int profileIndex,
        int itemIndex,
        EvaluationGroundTruth groundTruth,
        ClassifierRunStage stage)
    {
        if (profileIndex == 1 && itemIndex == 0)
        {
            return ClassifierRecommendation.NeedsReview;
        }

        if (profileIndex == 2 && stage == ClassifierRunStage.Holdout && groundTruth == EvaluationGroundTruth.Keep)
        {
            return ClassifierRecommendation.Unwanted;
        }

        if (profileIndex == 4 && itemIndex == 1 && groundTruth == EvaluationGroundTruth.Unwanted)
        {
            return ClassifierRecommendation.Keep;
        }

        return groundTruth == EvaluationGroundTruth.Keep
            ? ClassifierRecommendation.Keep
            : ClassifierRecommendation.Unwanted;
    }
}
