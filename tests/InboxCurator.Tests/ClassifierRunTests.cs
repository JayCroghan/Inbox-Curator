using InboxCurator.Classification;
using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InboxCurator.Tests;

public sealed class ClassifierRunTests
{
    [Fact]
    public async Task Executor_RunsCompleteModelBatchesSequentiallyUnloadsAndKeepsItemFailures()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var runId = await SeedRunAsync(store, lockedPrompt: false);
        var events = new List<string>();
        var classifier = new RecordingClassifier(events) { FailKey = "profile-a:sender-1@example.test" };
        var runtime = new RecordingRuntime(events, new HashSet<string> { "model-a", "model-b" });
        var executor = new ClassifierRunExecutor(
            store.Factory, classifier, runtime,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<ClassifierRunExecutor>.Instance);

        await executor.ExecuteAsync(runId, CancellationToken.None);

        Assert.Equal(
            [
                "residency:model-a",
                "unload:model-a",
                "classify:profile-a:list-0.example",
                "residency:model-a",
                "classify:profile-a:sender-1@example.test",
                "unload:model-a",
                "residency:model-b",
                "unload:model-b",
                "classify:profile-b:list-0.example",
                "residency:model-b",
                "classify:profile-b:sender-1@example.test",
                "unload:model-b"
            ],
            events);
        await using var db = await store.Factory.CreateDbContextAsync();
        var run = await db.ClassifierRuns.Include(item => item.Profiles).SingleAsync(item => item.Id == runId);
        Assert.Equal(ClassifierRunState.Completed, run.State);
        Assert.Equal(3, run.CompletedItems);
        Assert.Equal(1, run.FailedItems);
        Assert.All(run.Profiles, profile => Assert.Equal(ClassifierProfileState.Completed, profile.State));
        Assert.All(run.Profiles, profile => Assert.True(profile.PredominantlyVramResident));
        Assert.Equal(4, await db.ClassifierResults.CountAsync());
        var failed = await db.ClassifierResults.SingleAsync(item => item.Status == ClassifierResultStatus.RequestFailure);
        Assert.Equal("classifier_failure", failed.FailureCode);
    }

    [Fact]
    public async Task Executor_ResumeSkipsDurableProfileItemPairs()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var runId = await SeedRunAsync(store, lockedPrompt: false);
        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            var profile = await db.ClassifierRunProfiles.OrderBy(item => item.ExecutionOrder).FirstAsync();
            var item = await db.EvaluationCorpusItems.OrderBy(entry => entry.Id).FirstAsync();
            db.ClassifierResults.Add(new ClassifierResult
            {
                ClassifierRunId = runId,
                ClassifierRunProfileId = profile.Id,
                EvaluationCorpusItemId = item.Id,
                Status = ClassifierResultStatus.Completed,
                Recommendation = ClassifierRecommendation.Keep,
                Confidence = ClassifierConfidence.High,
                Category = ClassifierCategory.Professional,
                ReasonCodesJson = "[\"professional_content\"]",
                Rationale = "Previously completed durable result.",
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            });
            var run = await db.ClassifierRuns.SingleAsync(entry => entry.Id == runId);
            run.State = ClassifierRunState.Running;
            run.CompletedItems = 1;
            await db.SaveChangesAsync();
        }

        var events = new List<string>();
        var executor = new ClassifierRunExecutor(
            store.Factory,
            new RecordingClassifier(events),
            new RecordingRuntime(events, new HashSet<string> { "model-a", "model-b" }),
            TimeProvider.System,
            NullLogger<ClassifierRunExecutor>.Instance);
        await executor.ExecuteAsync(runId, CancellationToken.None);

        Assert.DoesNotContain("classify:profile-a:list-0.example", events);
        Assert.Equal(3, events.Count(item => item.StartsWith("classify:", StringComparison.Ordinal)));
        await using var verification = await store.Factory.CreateDbContextAsync();
        Assert.Equal(4, await verification.ClassifierResults.CountAsync());
        Assert.Equal(ClassifierRunState.Completed, (await verification.ClassifierRuns.FindAsync(runId))!.State);
    }

    [Fact]
    public async Task RunService_RequiresLockedPromptForHoldoutAndPersistsCancellation()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var runId = await SeedRunAsync(store, lockedPrompt: false, includeRun: false);
        var queue = new RecordingQueue();
        var service = new ClassifierRunService(
            store.Factory,
            Options.Create(new OllamaOptions
            {
                Profiles = [new OllamaModelProfile { Key = "profile-a", Model = "model-a" }]
            }),
            queue,
            TimeProvider.System);
        long corpusId;
        await using (var lookup = await store.Factory.CreateDbContextAsync())
        {
            corpusId = await lookup.EvaluationCorpora.Select(item => item.Id).SingleAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            ClassifierRunStage.Holdout, ["profile-a"], corpusId, ClassifierPromptDefinition.Version, CancellationToken.None));
        var created = await service.CreateAsync(
            ClassifierRunStage.DevelopmentValidation, ["profile-a"], corpusId, ClassifierPromptDefinition.Version, CancellationToken.None);
        Assert.True(queue.WakeCount > 0);

        await service.CancelAsync(created, CancellationToken.None);

        await using var db = await store.Factory.CreateDbContextAsync();
        var run = await db.ClassifierRuns.SingleAsync(item => item.Id == created);
        Assert.Equal(ClassifierRunState.CancelRequested, run.State);
        Assert.Equal(created, queue.CancelledRunId);
        Assert.Equal(2, run.TotalItems);
        Assert.Null(await db.ClusterDecisions.FirstOrDefaultAsync());
        Assert.NotEqual(runId, created);
    }

    [Fact]
    public async Task Executor_DoesNotLoadNextModelWhenExplicitUnloadFails()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var runId = await SeedRunAsync(store, lockedPrompt: false);
        var events = new List<string>();
        var runtime = new RecordingRuntime(events, new HashSet<string> { "model-a", "model-b" })
        {
            FailUnloadModel = "model-a"
        };
        var executor = new ClassifierRunExecutor(
            store.Factory,
            new RecordingClassifier(events),
            runtime,
            TimeProvider.System,
            NullLogger<ClassifierRunExecutor>.Instance);

        await executor.ExecuteAsync(runId, CancellationToken.None);

        Assert.Contains("unload:model-a", events);
        Assert.DoesNotContain(events, item => item.StartsWith("classify:profile-b", StringComparison.Ordinal));
        await using var db = await store.Factory.CreateDbContextAsync();
        var run = await db.ClassifierRuns.SingleAsync(item => item.Id == runId);
        Assert.Equal(ClassifierRunState.Failed, run.State);
        Assert.Equal("model_unload_failed", run.FailureCode);
    }

    [Fact]
    public async Task Executor_PersistsSeparateRepairMetricsAndCapturesColdLoadFromNormalizationFailure()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var runId = await SeedRunAsync(store, lockedPrompt: false);
        var events = new List<string>();
        var classifier = new RecordingClassifier(events)
        {
            NormalizationFailKey = "profile-a:list-0.example",
            SelfRepairKey = "profile-a:sender-1@example.test"
        };
        var executor = new ClassifierRunExecutor(
            store.Factory,
            classifier,
            new RecordingRuntime(events, new HashSet<string> { "model-a", "model-b" }),
            TimeProvider.System,
            NullLogger<ClassifierRunExecutor>.Instance);

        await executor.ExecuteAsync(runId, CancellationToken.None);

        await using var db = await store.Factory.CreateDbContextAsync();
        var profile = await db.ClassifierRunProfiles.SingleAsync(item => item.ClassifierRunId == runId && item.ProfileKey == "profile-a");
        Assert.Equal(240_000_000_000, profile.ColdLoadDurationNanoseconds);
        var results = await db.ClassifierResults
            .Where(item => item.ClassifierRunProfileId == profile.Id)
            .OrderBy(item => item.EvaluationCorpusItemId)
            .ToArrayAsync();
        Assert.Equal(ClassifierResultStatus.SchemaFailure, results[0].Status);
        Assert.Equal(ClassifierNormalizationMode.Failed, results[0].NormalizationMode);
        Assert.Equal("unusable primary response", results[0].PrimaryResponse);
        Assert.Equal("still unusable repair response", results[0].RepairResponse);
        Assert.True(results[0].IsColdLoadRequest);
        Assert.Equal(1_500_000_000, results[0].RepairTotalDurationNanoseconds);
        Assert.Equal(ClassifierResultStatus.Completed, results[1].Status);
        Assert.Equal(ClassifierNormalizationMode.SelfRepaired, results[1].NormalizationMode);
        Assert.Equal("{\"recommendation\":\"keep\"}", results[1].RepairResponse);
        Assert.False(results[1].IsColdLoadRequest);
        Assert.Equal(2_500_000_000, results[1].RepairTotalDurationNanoseconds);
    }

    [Fact]
    public async Task Executor_RefusesPersistedHoldoutWithoutCallingClassifierOrRuntime()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var runId = await SeedRunAsync(store, lockedPrompt: true);
        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            var run = await db.ClassifierRuns.SingleAsync(item => item.Id == runId);
            run.Stage = ClassifierRunStage.Holdout;
            await db.SaveChangesAsync();
        }

        var events = new List<string>();
        var executor = new ClassifierRunExecutor(
            store.Factory,
            new RecordingClassifier(events),
            new RecordingRuntime(events, new HashSet<string> { "model-a", "model-b" }),
            TimeProvider.System,
            NullLogger<ClassifierRunExecutor>.Instance);

        await executor.ExecuteAsync(runId, CancellationToken.None);

        Assert.Empty(events);
        await using var verification = await store.Factory.CreateDbContextAsync();
        var failed = await verification.ClassifierRuns.SingleAsync(item => item.Id == runId);
        Assert.Equal(ClassifierRunState.Failed, failed.State);
        Assert.Equal("holdout_disabled", failed.FailureCode);
        Assert.Empty(await verification.ClassifierResults.Where(item => item.ClassifierRunId == runId).ToArrayAsync());
    }

    [Fact]
    public async Task PromptLock_RequiresCompletedDevelopmentRunAndV3CanReuseExplicitCorpusWhileHoldoutStaysDisabled()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var promptService = new ClassifierPromptService(store.Factory, TimeProvider.System);
        await promptService.EnsureAllAsync();
        await using var promptLookup = await store.Factory.CreateDbContextAsync();
        var prompt = await promptLookup.ClassifierPromptVersions.SingleAsync(item => item.Version == ClassifierPromptDefinition.Version);
        long corpusAId;
        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            var corpusA = Corpus("corpus-a", DateTime.UtcNow.AddMinutes(-5));
            var developmentItem = Item(ClusterTargetType.ListId, "development-a.example", EvaluationGroundTruth.Keep);
            developmentItem.Split = EvaluationSplit.Development;
            var holdoutItem = Item(ClusterTargetType.Sender, "holdout-a@example.test", EvaluationGroundTruth.Unwanted);
            holdoutItem.Split = EvaluationSplit.Holdout;
            corpusA.Items.Add(developmentItem);
            corpusA.Items.Add(holdoutItem);
            db.EvaluationCorpora.Add(corpusA);
            await db.SaveChangesAsync();
            corpusAId = corpusA.Id;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => promptService.LockV1Async(corpusAId));

        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            db.ClassifierRuns.Add(new ClassifierRun
            {
                Id = "completed-development-a",
                EvaluationCorpusId = corpusAId,
                ClassifierPromptVersionId = prompt.Id,
                Stage = ClassifierRunStage.DevelopmentValidation,
                State = ClassifierRunState.Completed,
                TotalItems = 1,
                CompletedItems = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await promptService.LockV1Async(corpusAId);

        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            var locked = await db.ClassifierPromptVersions.SingleAsync(item => item.Id == prompt.Id);
            Assert.True(locked.IsLocked);
            Assert.Equal(corpusAId, locked.LockedEvaluationCorpusId);

            var corpusB = Corpus("corpus-b", DateTime.UtcNow.AddMinutes(5));
            var holdoutB = Item(ClusterTargetType.Sender, "holdout-b@example.test", EvaluationGroundTruth.Keep);
            holdoutB.Split = EvaluationSplit.Holdout;
            corpusB.Items.Add(holdoutB);
            db.EvaluationCorpora.Add(corpusB);
            await db.SaveChangesAsync();
        }

        var queue = new RecordingQueue();
        var runService = new ClassifierRunService(
            store.Factory,
            Options.Create(new OllamaOptions
            {
                Profiles = [new OllamaModelProfile { Key = "profile-a", Model = "model-a" }]
            }),
            queue,
            TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runService.CreateAsync(
            ClassifierRunStage.Holdout,
            ["profile-a"],
            corpusAId,
            ClassifierPromptV3Definition.Version,
            CancellationToken.None));
        var v3RunId = await runService.CreateAsync(
            ClassifierRunStage.DevelopmentValidation,
            ["profile-a"],
            corpusAId,
            ClassifierPromptV3Definition.Version,
            CancellationToken.None);

        await using var verification = await store.Factory.CreateDbContextAsync();
        var v3Run = await verification.ClassifierRuns
            .Include(item => item.ClassifierPromptVersion)
            .SingleAsync(item => item.Id == v3RunId);
        Assert.Equal(corpusAId, v3Run.EvaluationCorpusId);
        Assert.Equal(ClassifierPromptV3Definition.Version, v3Run.ClassifierPromptVersion.Version);
        Assert.Equal(1, v3Run.TotalItems);
    }

    private static async Task<string> SeedRunAsync(SqliteTestStore store, bool lockedPrompt, bool includeRun = true)
    {
        await using var db = await store.Factory.CreateDbContextAsync();
        var corpus = new EvaluationCorpus
        {
            Version = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTime.UtcNow,
            SplitStrategy = EvaluationCorpusService.SplitStrategy,
            EligibleSourceCount = 2
        };
        corpus.Items.Add(Item(ClusterTargetType.ListId, "list-0.example", EvaluationGroundTruth.Keep));
        corpus.Items.Add(Item(ClusterTargetType.Sender, "sender-1@example.test", EvaluationGroundTruth.Unwanted));
        var prompt = new ClassifierPromptVersion
        {
            Version = ClassifierPromptDefinition.Version,
            SystemPrompt = ClassifierPromptDefinition.SystemPrompt,
            SystemPromptSha256 = ClassifierPromptDefinition.SystemPromptSha256,
            OutputSchemaVersion = ClassifierPromptDefinition.OutputSchemaVersion,
            OutputJsonSchema = ClassifierPromptDefinition.OutputJsonSchema,
            IsLocked = lockedPrompt,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.EvaluationCorpora.Add(corpus);
        db.ClassifierPromptVersions.Add(prompt);
        var runId = "run-" + Guid.NewGuid().ToString("N")[..12];
        if (includeRun)
        {
            var run = new ClassifierRun
            {
                Id = runId,
                EvaluationCorpus = corpus,
                ClassifierPromptVersion = prompt,
                Stage = ClassifierRunStage.DevelopmentValidation,
                State = ClassifierRunState.Queued,
                TotalItems = 4,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            run.Profiles.Add(Profile(runId, "profile-a", "model-a", 0));
            run.Profiles.Add(Profile(runId, "profile-b", "model-b", 1));
            db.ClassifierRuns.Add(run);
        }

        await db.SaveChangesAsync();
        return runId;
    }

    private static EvaluationCorpusItem Item(ClusterTargetType targetType, string targetValue, EvaluationGroundTruth truth) => new()
    {
        TargetType = targetType,
        TargetValue = targetValue,
        GroupKey = targetType == ClusterTargetType.ListId ? $"list:{targetValue}" : $"sender:{targetValue}",
        DisplayName = targetValue,
        GroundTruth = truth,
        Split = EvaluationSplit.Development,
        MessageCount = 10,
        FirstReceivedUtc = DateTime.UtcNow.AddDays(-10),
        LastReceivedUtc = DateTime.UtcNow,
        RepresentativeSubjectsJson = "[\"Synthetic subject\"]"
    };

    private static EvaluationCorpus Corpus(string version, DateTime createdAtUtc) => new()
    {
        Version = version,
        CreatedAtUtc = createdAtUtc,
        SplitStrategy = EvaluationCorpusService.SplitStrategy,
        EligibleSourceCount = 2
    };

    private static ClassifierRunProfile Profile(string runId, string key, string model, int order) => new()
    {
        ClassifierRunId = runId,
        ProfileKey = key,
        ModelName = model,
        Temperature = 0,
        ContextLength = 8192,
        Stream = false,
        KeepAlive = "30m",
        ExecutionOrder = order,
        State = ClassifierProfileState.Pending
    };

    private sealed class RecordingClassifier(List<string> events) : IClusterClassifier
    {
        public string? FailKey { get; init; }
        public string? NormalizationFailKey { get; init; }
        public string? SelfRepairKey { get; init; }

        public Task<ClusterClassifierResponse> ClassifyAsync(
            ClassifierModelProfile profile,
            ClassifierPromptSnapshot prompt,
            ClusterClassifierInput input,
            CancellationToken cancellationToken)
        {
            var key = $"{profile.Key}:{input.TargetValue}";
            events.Add($"classify:{key}");
            if (key == FailKey)
            {
                throw new InvalidOperationException("Synthetic item failure.");
            }

            if (key == NormalizationFailKey)
            {
                return Task.FromResult(new ClusterClassifierResponse(
                    null,
                    "unusable primary response",
                    "still unusable repair response",
                    null,
                    ["unknown_reason"],
                    ClassifierNormalizationMode.Failed,
                    ["invalid_json"],
                    [],
                    "normalization_failed",
                    "repair_invalid_json",
                    new ClassifierResponseMetrics(242_000_000_000, 240_000_000_000, 600, 700_000_000, 80, 1_000_000_000, true, 999),
                    new ClassifierResponseMetrics(1_500_000_000, 1_000_000, 120, 200_000_000, 20, 500_000_000, false, 0)));
            }

            if (key == SelfRepairKey)
            {
                return Task.FromResult(new ClusterClassifierResponse(
                    new ValidatedClassifierOutput(
                        ClassifierRecommendation.Keep,
                        ClassifierConfidence.Low,
                        ClassifierCategory.Professional,
                        [ClassifierReasonCode.ProfessionalContent],
                        "The original explanation remains visible."),
                    "{\"recommendation\":\"retain\",\"explanation\":\"The original explanation remains visible.\"}",
                    "{\"recommendation\":\"keep\"}",
                    "The original explanation remains visible.",
                    ["professional_content"],
                    ClassifierNormalizationMode.SelfRepaired,
                    ["recommendation_unrecognized"],
                    [],
                    null,
                    null,
                    new ClassifierResponseMetrics(4_000_000_000, 1_000_000, 600, 700_000_000, 80, 1_000_000_000, false, 0),
                    new ClassifierResponseMetrics(2_500_000_000, 1_000_000, 150, 200_000_000, 25, 600_000_000, false, 0)));
            }

            return Task.FromResult(new ClusterClassifierResponse(
                new ValidatedClassifierOutput(
                    ClassifierRecommendation.Keep,
                    ClassifierConfidence.Medium,
                    ClassifierCategory.Professional,
                    [ClassifierReasonCode.ProfessionalContent],
                    "Synthetic valid result."),
                "{\"recommendation\":\"keep\"}",
                null,
                "Synthetic valid result.",
                ["professional_content"],
                ClassifierNormalizationMode.Direct,
                [],
                [],
                null,
                null,
                new ClassifierResponseMetrics(2_000_000_000, 1_000_000_000, 600, 700_000_000, 80, 1_000_000_000, false, 0),
                null));
        }
    }

    private sealed class RecordingRuntime(List<string> events, IReadOnlySet<string> installed) : ILocalModelRuntime
    {
        public string? FailUnloadModel { get; init; }

        public Task<IReadOnlySet<string>> GetInstalledModelsAsync(CancellationToken cancellationToken) => Task.FromResult(installed);

        public Task<ModelResidency?> GetResidencyAsync(string model, CancellationToken cancellationToken)
        {
            events.Add($"residency:{model}");
            return Task.FromResult<ModelResidency?>(new ModelResidency(model, 100, 95, 8192));
        }

        public Task UnloadAsync(string model, CancellationToken cancellationToken)
        {
            events.Add($"unload:{model}");
            if (model == FailUnloadModel)
            {
                throw new InvalidOperationException("Synthetic unload failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingQueue : IClassifierRunQueue
    {
        public int WakeCount { get; private set; }
        public string? CancelledRunId { get; private set; }
        public void Wake() => WakeCount++;
        public void Cancel(string runId) => CancelledRunId = runId;
    }
}
