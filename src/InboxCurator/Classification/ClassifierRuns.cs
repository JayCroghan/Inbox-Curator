using System.Text.Json;
using System.Threading.Channels;
using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InboxCurator.Classification;

public interface IClassifierRunQueue
{
    void Wake();
    void Cancel(string runId);
}

public sealed class ClassifierRunService(
    IDbContextFactory<InboxCuratorDbContext> dbFactory,
    IOptions<OllamaOptions> options,
    IClassifierRunQueue queue,
    TimeProvider timeProvider)
{
    public async Task<string> CreateAsync(
        ClassifierRunStage stage,
        IReadOnlyCollection<string> selectedProfileKeys,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var prompt = await db.ClassifierPromptVersions
            .SingleAsync(item => item.Version == ClassifierPromptDefinition.Version, cancellationToken);
        EvaluationCorpus? corpus;
        if (stage == ClassifierRunStage.Holdout)
        {
            if (!prompt.IsLocked || !prompt.LockedEvaluationCorpusId.HasValue)
            {
                throw new InvalidOperationException("The prompt must be locked to a completed development corpus before running holdout.");
            }

            corpus = await db.EvaluationCorpora.SingleOrDefaultAsync(
                item => item.Id == prompt.LockedEvaluationCorpusId.Value,
                cancellationToken);
            if (corpus is null)
            {
                throw new InvalidOperationException("The prompt's locked evaluation corpus is unavailable.");
            }
        }
        else
        {
            corpus = await db.EvaluationCorpora
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (corpus is null)
        {
            throw new InvalidOperationException("Create an evaluation corpus before starting a run.");
        }

        var configured = options.Value.Profiles
            .Select((profile, index) => new { Profile = profile, Index = index })
            .Where(item => selectedProfileKeys.Contains(item.Profile.Key, StringComparer.Ordinal))
            .ToArray();
        if (configured.Length == 0)
        {
            throw new InvalidOperationException("Select at least one configured model profile.");
        }

        if (configured.Length != selectedProfileKeys.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidOperationException("One or more selected model profiles are not configured.");
        }

        var splitItems = stage == ClassifierRunStage.Holdout
            ? await db.EvaluationCorpusItems.CountAsync(item =>
                item.EvaluationCorpusId == corpus.Id && item.Split == EvaluationSplit.Holdout, cancellationToken)
            : await db.EvaluationCorpusItems.CountAsync(item =>
                item.EvaluationCorpusId == corpus.Id && item.Split != EvaluationSplit.Holdout, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var run = new ClassifierRun
        {
            Id = Guid.NewGuid().ToString("N"),
            EvaluationCorpusId = corpus.Id,
            ClassifierPromptVersionId = prompt.Id,
            Stage = stage,
            State = ClassifierRunState.Queued,
            TotalItems = splitItems * configured.Length,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        foreach (var entry in configured)
        {
            run.Profiles.Add(new ClassifierRunProfile
            {
                ClassifierRunId = run.Id,
                ProfileKey = entry.Profile.Key,
                ModelName = entry.Profile.Model,
                ThinkMode = entry.Profile.Think,
                Temperature = entry.Profile.Temperature,
                ContextLength = entry.Profile.NumCtx,
                Stream = entry.Profile.Stream,
                KeepAlive = entry.Profile.KeepAlive,
                ExecutionOrder = entry.Index,
                State = ClassifierProfileState.Pending
            });
        }

        db.ClassifierRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        queue.Wake();
        return run.Id;
    }

    public async Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.ClassifierRuns.FindAsync([runId], cancellationToken);
        if (run is null || run.State is ClassifierRunState.Completed or ClassifierRunState.Cancelled or ClassifierRunState.Failed)
        {
            return;
        }

        run.State = ClassifierRunState.CancelRequested;
        run.CancelRequestedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        run.UpdatedAtUtc = run.CancelRequestedAtUtc.Value;
        await db.SaveChangesAsync(cancellationToken);
        queue.Cancel(runId);
    }
}

public sealed class ClassifierRunExecutor(
    IDbContextFactory<InboxCuratorDbContext> dbFactory,
    IClusterClassifier classifier,
    ILocalModelRuntime runtime,
    TimeProvider timeProvider,
    ILogger<ClassifierRunExecutor> logger)
{
    public async Task ExecuteAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            var installedModels = await runtime.GetInstalledModelsAsync(cancellationToken);
            await MarkRunStartedAsync(runId, cancellationToken);
            var profiles = await LoadProfilesAsync(runId, cancellationToken);
            foreach (var profile in profiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!installedModels.Contains(profile.ModelName))
                {
                    await RecordMissingProfileAsync(runId, profile.Id, cancellationToken);
                    continue;
                }

                await ExecuteProfileAsync(runId, profile.Id, cancellationToken);
            }

            await CompleteRunAsync(runId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordCancellationAsync(runId);
            throw;
        }
        catch (Exception exception)
        {
            await FailRunAsync(runId, FailureCode(exception));
            logger.LogWarning("Classifier run {RunId} failed with {FailureCode}.", runId, FailureCode(exception));
        }
    }

    private async Task ExecuteProfileAsync(string runId, long profileId, CancellationToken cancellationToken)
    {
        ClassifierModelProfile profile;
        ClassifierPromptSnapshot prompt;
        long[] itemIds;
        bool freshProfile;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var run = await db.ClassifierRuns
                .Include(item => item.ClassifierPromptVersion)
                .SingleAsync(item => item.Id == runId, cancellationToken);
            var storedProfile = await db.ClassifierRunProfiles.SingleAsync(item => item.Id == profileId, cancellationToken);
            freshProfile = storedProfile.State == ClassifierProfileState.Pending &&
                !await db.ClassifierResults.AnyAsync(item => item.ClassifierRunProfileId == profileId, cancellationToken);
            storedProfile.State = ClassifierProfileState.Running;
            storedProfile.StartedAtUtc ??= timeProvider.GetUtcNow().UtcDateTime;
            run.CurrentProfileKey = storedProfile.ProfileKey;
            run.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);

            profile = ToProfile(storedProfile);
            prompt = new ClassifierPromptSnapshot(
                run.ClassifierPromptVersion.Version,
                run.ClassifierPromptVersion.SystemPrompt,
                run.ClassifierPromptVersion.SystemPromptSha256,
                run.ClassifierPromptVersion.OutputSchemaVersion,
                run.ClassifierPromptVersion.OutputJsonSchema);
            var itemQuery = db.EvaluationCorpusItems.Where(item => item.EvaluationCorpusId == run.EvaluationCorpusId);
            itemQuery = run.Stage == ClassifierRunStage.Holdout
                ? itemQuery.Where(item => item.Split == EvaluationSplit.Holdout)
                : itemQuery.Where(item => item.Split != EvaluationSplit.Holdout);
            itemIds = await itemQuery
                .OrderBy(item => item.Split)
                .ThenBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync(cancellationToken);
        }

        if (freshProfile)
        {
            await EnsureColdStartAsync(profile, cancellationToken);
        }

        // The first newly executed request is a cold-start observation even after a
        // process resume. Durable results skipped below do not consume this marker.
        var needsColdObservation = true;
        try
        {
            foreach (var itemId in itemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                if (await db.ClassifierResults.AnyAsync(item =>
                    item.ClassifierRunProfileId == profileId && item.EvaluationCorpusItemId == itemId, cancellationToken))
                {
                    continue;
                }

                var item = await db.EvaluationCorpusItems.SingleAsync(entry => entry.Id == itemId, cancellationToken);
                var run = await db.ClassifierRuns.SingleAsync(entry => entry.Id == runId, cancellationToken);
                if (run.State == ClassifierRunState.CancelRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                run.CurrentSplit = item.Split;
                run.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                var result = await ClassifyItemAsync(runId, profileId, item, profile, prompt, needsColdObservation, cancellationToken);
                db.ClassifierResults.Add(result);
                if (result.Status == ClassifierResultStatus.Completed)
                {
                    run.CompletedItems++;
                }
                else
                {
                    run.FailedItems++;
                }

                // Once inference returned, preserve that completed observation even if cancellation
                // arrived between the response and this durable item boundary.
                await db.SaveChangesAsync(CancellationToken.None);
                if (needsColdObservation && result.Status == ClassifierResultStatus.Completed)
                {
                    await CaptureResidencyAsync(profileId, profile.Model, result.LoadDurationNanoseconds, cancellationToken);
                    needsColdObservation = false;
                }
            }

            await using var completionDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            var completedProfile = await completionDb.ClassifierRunProfiles.SingleAsync(item => item.Id == profileId, cancellationToken);
            completedProfile.State = ClassifierProfileState.Completed;
            completedProfile.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await completionDb.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            using var unloadTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await runtime.UnloadAsync(profile.Model, unloadTimeout.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || unloadTimeout.IsCancellationRequested)
            {
                logger.LogWarning("Model profile {ProfileKey} could not be explicitly unloaded: {FailureCode}.",
                    profile.Key, FailureCode(exception));
                if (!cancellationToken.IsCancellationRequested)
                {
                    throw new OllamaRequestException("model_unload_failed", exception);
                }
            }
        }
    }

    private async Task EnsureColdStartAsync(ClassifierModelProfile profile, CancellationToken cancellationToken)
    {
        var residency = await runtime.GetResidencyAsync(profile.Model, cancellationToken);
        if (residency is null)
        {
            return;
        }

        using var unloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        unloadTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await runtime.UnloadAsync(profile.Model, unloadTimeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OllamaRequestException("model_unload_failed", exception);
        }
    }

    private async Task<ClassifierResult> ClassifyItemAsync(
        string runId,
        long profileId,
        EvaluationCorpusItem item,
        ClassifierModelProfile profile,
        ClassifierPromptSnapshot prompt,
        bool isColdLoad,
        CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var response = await classifier.ClassifyAsync(
                profile,
                prompt,
                ClassifierInputFactory.Create(item),
                cancellationToken);
            return new ClassifierResult
            {
                ClassifierRunId = runId,
                ClassifierRunProfileId = profileId,
                EvaluationCorpusItemId = item.Id,
                Status = ClassifierResultStatus.Completed,
                Recommendation = response.Output.Recommendation,
                Confidence = response.Output.Confidence,
                Category = response.Output.Category,
                ReasonCodesJson = JsonSerializer.Serialize(response.Output.ReasonCodes.Select(ClassifierOutputValidator.ReasonCodeValue)),
                Rationale = response.Output.Rationale,
                ThinkingPresent = response.Metrics.ThinkingPresent,
                ThinkingCharacterCount = response.Metrics.ThinkingCharacterCount,
                IsColdLoadRequest = isColdLoad,
                TotalDurationNanoseconds = response.Metrics.TotalDurationNanoseconds,
                LoadDurationNanoseconds = response.Metrics.LoadDurationNanoseconds,
                PromptEvalCount = response.Metrics.PromptEvalCount,
                PromptEvalDurationNanoseconds = response.Metrics.PromptEvalDurationNanoseconds,
                EvalCount = response.Metrics.EvalCount,
                EvalDurationNanoseconds = response.Metrics.EvalDurationNanoseconds,
                StartedAtUtc = started,
                CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };
        }
        catch (ClassifierSchemaException exception)
        {
            return FailedResult(runId, profileId, item.Id, ClassifierResultStatus.SchemaFailure,
                $"schema_{exception.Code}", isColdLoad, started);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return FailedResult(runId, profileId, item.Id, ClassifierResultStatus.RequestFailure,
                FailureCode(exception), isColdLoad, started);
        }
    }

    private ClassifierResult FailedResult(
        string runId,
        long profileId,
        long itemId,
        ClassifierResultStatus status,
        string failureCode,
        bool isColdLoad,
        DateTime started) => new()
        {
            ClassifierRunId = runId,
            ClassifierRunProfileId = profileId,
            EvaluationCorpusItemId = itemId,
            Status = status,
            FailureCode = failureCode,
            IsColdLoadRequest = isColdLoad,
            StartedAtUtc = started,
            CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

    private async Task CaptureResidencyAsync(long profileId, string model, long? loadDuration, CancellationToken cancellationToken)
    {
        ModelResidency? residency = null;
        try
        {
            residency = await runtime.GetResidencyAsync(model, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("VRAM residency for profile {ProfileId} was unavailable: {FailureCode}.",
                profileId, FailureCode(exception));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.ClassifierRunProfiles.SingleAsync(item => item.Id == profileId, cancellationToken);
        profile.ColdLoadDurationNanoseconds ??= loadDuration;
        if (residency is not null)
        {
            profile.ModelSizeBytes = residency.SizeBytes;
            profile.SizeVramBytes = residency.SizeVramBytes;
            profile.RuntimeContextLength = residency.ContextLength;
            profile.PredominantlyVramResident = residency.PredominantlyVramResident;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordMissingProfileAsync(string runId, long profileId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.ClassifierRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        var profile = await db.ClassifierRunProfiles.SingleAsync(item => item.Id == profileId, cancellationToken);
        profile.State = ClassifierProfileState.Missing;
        profile.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var query = db.EvaluationCorpusItems.Where(item => item.EvaluationCorpusId == run.EvaluationCorpusId);
        query = run.Stage == ClassifierRunStage.Holdout
            ? query.Where(item => item.Split == EvaluationSplit.Holdout)
            : query.Where(item => item.Split != EvaluationSplit.Holdout);
        var already = await db.ClassifierResults
            .Where(item => item.ClassifierRunProfileId == profileId)
            .Select(item => item.EvaluationCorpusItemId)
            .ToArrayAsync(cancellationToken);
        var remaining = await query.Where(item => !already.Contains(item.Id)).Select(item => item.Id).ToArrayAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var itemId in remaining)
        {
            db.ClassifierResults.Add(new ClassifierResult
            {
                ClassifierRunId = runId,
                ClassifierRunProfileId = profileId,
                EvaluationCorpusItemId = itemId,
                Status = ClassifierResultStatus.RequestFailure,
                FailureCode = "model_missing",
                StartedAtUtc = now,
                CompletedAtUtc = now
            });
        }

        run.FailedItems += remaining.Length;
        run.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkRunStartedAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.ClassifierRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        if (run.State == ClassifierRunState.CancelRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        run.State = ClassifierRunState.Running;
        run.StartedAtUtc ??= timeProvider.GetUtcNow().UtcDateTime;
        run.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ClassifierRunProfile[]> LoadProfilesAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ClassifierRunProfiles
            .Where(item => item.ClassifierRunId == runId && item.State != ClassifierProfileState.Completed && item.State != ClassifierProfileState.Missing)
            .OrderBy(item => item.ExecutionOrder)
            .ToArrayAsync(cancellationToken);
    }

    private async Task CompleteRunAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.ClassifierRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        run.State = ClassifierRunState.Completed;
        run.CurrentProfileKey = null;
        run.CurrentSplit = null;
        run.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        run.UpdatedAtUtc = run.CompletedAtUtc.Value;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordCancellationAsync(string runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await db.ClassifierRuns.SingleOrDefaultAsync(item => item.Id == runId);
        if (run is null || run.State != ClassifierRunState.CancelRequested)
        {
            return;
        }

        run.State = ClassifierRunState.Cancelled;
        run.CurrentProfileKey = null;
        run.CurrentSplit = null;
        run.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        run.UpdatedAtUtc = run.CompletedAtUtc.Value;
        var runningProfiles = await db.ClassifierRunProfiles
            .Where(item => item.ClassifierRunId == runId && item.State == ClassifierProfileState.Running)
            .ToArrayAsync();
        foreach (var profile in runningProfiles)
        {
            profile.State = ClassifierProfileState.Cancelled;
            profile.CompletedAtUtc = run.CompletedAtUtc;
        }

        await db.SaveChangesAsync();
    }

    private async Task FailRunAsync(string runId, string failureCode)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var run = await db.ClassifierRuns.SingleOrDefaultAsync(item => item.Id == runId);
        if (run is null)
        {
            return;
        }

        run.State = ClassifierRunState.Failed;
        run.FailureCode = failureCode;
        run.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        run.UpdatedAtUtc = run.CompletedAtUtc.Value;
        await db.SaveChangesAsync();
    }

    private static ClassifierModelProfile ToProfile(ClassifierRunProfile profile) => new(
        profile.ProfileKey,
        profile.ModelName,
        profile.ThinkMode,
        profile.Temperature,
        profile.ContextLength,
        profile.Stream,
        profile.KeepAlive);

    private static string FailureCode(Exception exception) => exception switch
    {
        OllamaRequestException request => request.Code,
        ClassifierSchemaException schema => $"schema_{schema.Code}",
        HttpRequestException => "ollama_unreachable",
        TimeoutException => "ollama_timeout",
        _ => "classifier_failure"
    };
}

public sealed class ClassifierRunCoordinator(
    ClassifierRunExecutor executor,
    IDbContextFactory<InboxCuratorDbContext> dbFactory,
    ILogger<ClassifierRunCoordinator> logger) : BackgroundService, IClassifierRunQueue
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly object _activeLock = new();
    private string? _activeRunId;
    private CancellationTokenSource? _activeCancellation;

    public void Wake() => _wake.Writer.TryWrite(true);

    public void Cancel(string runId)
    {
        lock (_activeLock)
        {
            if (string.Equals(_activeRunId, runId, StringComparison.Ordinal))
            {
                _activeCancellation?.Cancel();
            }
        }

        Wake();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Wake();
        await foreach (var _ in _wake.Reader.ReadAllAsync(stoppingToken))
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var runId = await FindNextRunAsync(stoppingToken);
                if (runId is null)
                {
                    break;
                }

                using var active = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                lock (_activeLock)
                {
                    _activeRunId = runId;
                    _activeCancellation = active;
                }

                try
                {
                    await executor.ExecuteAsync(runId, active.Token);
                }
                catch (OperationCanceledException) when (active.IsCancellationRequested)
                {
                    logger.LogInformation("Classifier run {RunId} stopped by cancellation.", runId);
                }
                finally
                {
                    lock (_activeLock)
                    {
                        _activeRunId = null;
                        _activeCancellation = null;
                    }
                }
            }
        }
    }

    private async Task<string?> FindNextRunAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cancelled = await db.ClassifierRuns
            .Where(item => item.State == ClassifierRunState.CancelRequested)
            .ToArrayAsync(cancellationToken);
        if (cancelled.Length > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var run in cancelled)
            {
                run.State = ClassifierRunState.Cancelled;
                run.CompletedAtUtc = now;
                run.UpdatedAtUtc = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return await db.ClassifierRuns
            .Where(item => item.State == ClassifierRunState.Queued || item.State == ClassifierRunState.Running)
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
