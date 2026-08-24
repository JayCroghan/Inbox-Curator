using System.Text.RegularExpressions;
using InboxCurator.Classification;
using InboxCurator.Data;
using InboxCurator.Gmail;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InboxCurator.Tests;

public sealed class ClassifierWebTests
{
    [Fact]
    public async Task ClassifierLab_DefaultsToV2ExplicitCorpusAndNeverExposesHoldoutOrCallsGmail()
    {
        using var factory = new ClassifierFactory();
        using var client = factory.CreateClient();

        var initial = await client.GetStringAsync("/Classifier");
        Assert.Contains("Classifier Lab", initial, StringComparison.Ordinal);
        Assert.Contains(ClassifierPromptV2Definition.Version, initial, StringComparison.Ordinal);
        Assert.Contains(ClassifierPromptV2Definition.RepairPromptVersion, initial, StringComparison.Ordinal);
        Assert.Contains("Human KEEP", initial, StringComparison.Ordinal);
        Assert.Contains("Model UNWANTED", initial, StringComparison.Ordinal);
        Assert.Contains("Holdout execution is disabled in MAIL-003A.1", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Stage\" value=\"Holdout\"", initial, StringComparison.Ordinal);
        Assert.Empty(factory.Gmail.ListRequests);

        int decisionCount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InboxCuratorDbContext>>();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                decisionCount = await db.ClusterDecisions.CountAsync();
            }

            var corpus = await scope.ServiceProvider.GetRequiredService<EvaluationCorpusService>().CreateAsync();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var prompt = await db.ClassifierPromptVersions.SingleAsync(
                    item => item.Version == ClassifierPromptV2Definition.Version);
                db.ClassifierRuns.Add(new ClassifierRun
                {
                    Id = "web-completed-development",
                    EvaluationCorpusId = corpus.Id,
                    ClassifierPromptVersionId = prompt.Id,
                    Stage = ClassifierRunStage.DevelopmentValidation,
                    State = ClassifierRunState.Completed,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await scope.ServiceProvider.GetRequiredService<ClassifierPromptService>()
                .LockAsync(ClassifierPromptV2Definition.Version, corpus.Id);
        }

        var locked = await client.GetStringAsync("/Classifier");
        Assert.Contains("Locked", locked, StringComparison.Ordinal);
        Assert.Contains("Pinned corpus:", locked, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Stage\" value=\"Holdout\"", locked, StringComparison.Ordinal);
        Assert.Empty(factory.Gmail.ListRequests);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InboxCuratorDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            Assert.Equal(decisionCount, await db.ClusterDecisions.CountAsync());
        }
    }

    [Fact]
    public async Task ClassifierLab_RendersOnlyLocalProfileControlsAndNoRecommendationAcceptance()
    {
        using var factory = new ClassifierFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Classifier");

        Assert.Contains("qwen36-35b-nothink", html, StringComparison.Ordinal);
        Assert.Contains("gemma4-31b-default", html, StringComparison.Ordinal);
        Assert.Contains("deepseek-r1-32b-thinking", html, StringComparison.Ordinal);
        Assert.Contains("ornith-15-35b-default", html, StringComparison.Ordinal);
        Assert.Contains("gpt-oss-low", html, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1 only", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Accept recommendation", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gmail.modify", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyntheticEvaluation_RendersV2NormalizationAndReadOnlyDisagreementEvidence()
    {
        using var factory = new ClassifierFactory(seedEvaluation: true);
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/Classifier");

        Assert.Contains("Preflight", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ClassifierPromptV2Definition.Version, html, StringComparison.Ordinal);
        Assert.Contains("NormalizeRepairV2", html, StringComparison.Ordinal);
        Assert.Contains("Repaired", html, StringComparison.Ordinal);
        Assert.Contains("Disagreements &amp; failures", html, StringComparison.Ordinal);
        Assert.Contains("Self-repaired results", html, StringComparison.Ordinal);
        Assert.Contains("Semantic warnings", html, StringComparison.Ordinal);
        Assert.Contains("Normalization warnings", html, StringComparison.Ordinal);
        Assert.Contains("All evaluated results", html, StringComparison.Ordinal);
        Assert.Contains("deepseek-r1-32b-thinking", html, StringComparison.Ordinal);
        Assert.Contains("frozen evidence only", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Primary explanation", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Accept recommendation", html, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(factory.Gmail.ListRequests);
    }

    [Fact]
    public async Task InspectionViews_IncludeAgreeingRepairedAndWarnedResultsWithoutMutatingMailboxState()
    {
        using var factory = new ClassifierFactory(seedEvaluation: true);
        using var client = factory.CreateClient();
        string runId;
        long profileId;
        string displayName;
        string disagreementDisplayName;
        int decisionCount;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InboxCuratorDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            decisionCount = await db.ClusterDecisions.CountAsync();
            var result = await db.ClassifierResults
                .Include(item => item.EvaluationCorpusItem)
                .Include(item => item.ClassifierRun)
                    .ThenInclude(item => item.ClassifierPromptVersion)
                .Where(item => item.ClassifierRun.ClassifierPromptVersion.Version == ClassifierPromptV2Definition.Version &&
                    item.Status == ClassifierResultStatus.Completed &&
                    ((item.EvaluationCorpusItem.GroundTruth == EvaluationGroundTruth.Keep && item.Recommendation == ClassifierRecommendation.Keep) ||
                     (item.EvaluationCorpusItem.GroundTruth == EvaluationGroundTruth.Unwanted && item.Recommendation == ClassifierRecommendation.Unwanted)))
                .FirstAsync();
            runId = result.ClassifierRunId;
            profileId = result.ClassifierRunProfileId;
            displayName = result.EvaluationCorpusItem.DisplayName;
            result.NormalizationMode = ClassifierNormalizationMode.SelfRepaired;
            result.PrimaryExplanation = "Primary <evidence> remains unchanged.";
            result.PrimaryResponse = "{\"recommendation\":\"noncanonical\",\"explanation\":\"<script>primary</script>\"}";
            var repairRecommendation = result.Recommendation == ClassifierRecommendation.Keep ? "keep" : "unwanted";
            result.RepairResponse = $"{{\"recommendation\":\"{repairRecommendation}\",\"extra\":\"<script>repair</script>\"}}";
            result.NormalizationWarningsJson = "[\"recommendation_unrecognized\"]";
            result.SemanticWarningsJson = "[\"test_semantic_warning\"]";
            var disagreement = await db.ClassifierResults
                .Include(item => item.EvaluationCorpusItem)
                .Where(item => item.ClassifierRunProfileId == profileId && item.Id != result.Id)
                .FirstAsync();
            disagreement.Status = ClassifierResultStatus.Completed;
            disagreement.Recommendation = disagreement.EvaluationCorpusItem.GroundTruth == EvaluationGroundTruth.Keep
                ? ClassifierRecommendation.Unwanted
                : ClassifierRecommendation.Keep;
            disagreementDisplayName = disagreement.EvaluationCorpusItem.DisplayName;
            await db.SaveChangesAsync();
        }

        var defaultView = await client.GetStringAsync($"/Classifier?runId={runId}&profileId={profileId}");
        Assert.Contains("Disagreements &amp; failures", defaultView, StringComparison.Ordinal);
        Assert.DoesNotContain(displayName, defaultView, StringComparison.Ordinal);
        Assert.Contains(disagreementDisplayName, defaultView, StringComparison.Ordinal);

        var repairedView = await client.GetStringAsync(
            $"/Classifier?runId={runId}&profileId={profileId}&inspectionMode=SelfRepaired");
        Assert.Contains(displayName, repairedView, StringComparison.Ordinal);
        Assert.Contains("Stored primary final response", repairedView, StringComparison.Ordinal);
        Assert.Contains("Stored repair final response", repairedView, StringComparison.Ordinal);
        Assert.Contains("Primary &lt;evidence&gt; remains unchanged.", repairedView, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;repair&lt;/script&gt;", repairedView, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>repair</script>", repairedView, StringComparison.Ordinal);

        var semanticView = await client.GetStringAsync(
            $"/Classifier?runId={runId}&profileId={profileId}&inspectionMode=SemanticWarnings");
        Assert.Contains(displayName, semanticView, StringComparison.Ordinal);
        Assert.Contains("test_semantic_warning", semanticView, StringComparison.Ordinal);

        var normalizationView = await client.GetStringAsync(
            $"/Classifier?runId={runId}&profileId={profileId}&inspectionMode=NormalizationWarnings");
        Assert.Contains(displayName, normalizationView, StringComparison.Ordinal);
        Assert.Contains("recommendation_unrecognized", normalizationView, StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InboxCuratorDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            Assert.Equal(decisionCount, await db.ClusterDecisions.CountAsync());
        }

        Assert.Empty(factory.Gmail.ListRequests);
    }

    private sealed class ClassifierFactory(bool seedEvaluation = false) : WebApplicationFactory<Program>
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"inbox-curator-classifier-web-{Guid.NewGuid():N}.db");
        public FakeGmailMailboxClient Gmail { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:InboxCurator"] = $"Data Source={_databasePath}",
                    ["SeedSyntheticData"] = "true",
                    ["SeedSyntheticEvaluationData"] = seedEvaluation.ToString(),
                    ["Gmail:ScanOnStartup"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGmailMailboxClient>();
                services.AddSingleton<IGmailMailboxClient>(Gmail);
                services.RemoveAll<ILocalModelRuntime>();
                services.AddSingleton<ILocalModelRuntime>(new InstalledRuntime());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(
                    new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            if (disposing && File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }

    private sealed class InstalledRuntime : ILocalModelRuntime
    {
        private static readonly IReadOnlySet<string> Models = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "qwen3.6:35b", "gemma4:31b", "deepseek-r1:32b", "ornith-1.5:35b", "gpt-oss:latest"
        };

        public Task<IReadOnlySet<string>> GetInstalledModelsAsync(CancellationToken cancellationToken) => Task.FromResult(Models);
        public Task<ModelResidency?> GetResidencyAsync(string model, CancellationToken cancellationToken) => Task.FromResult<ModelResidency?>(null);
        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
