using InboxCurator.Data;
using InboxCurator.Services;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Tests;

public sealed class ClusterDecisionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ClusterDecisionKind.KeepProtect, false, true)]
    [InlineData(ClusterDecisionKind.UnwantedExistingAndFuture, false, true)]
    [InlineData(ClusterDecisionKind.CleanExistingOnly, false, false)]
    [InlineData(ClusterDecisionKind.CleanOlderThan, true, false)]
    [InlineData(ClusterDecisionKind.Defer, false, false)]
    public async Task SetAsync_CreatesEveryDecisionKindWithExpectedSemantics(
        ClusterDecisionKind decisionKind,
        bool needsCutoff,
        bool appliesToFuture)
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "m1", ClusterTargetType.ListId, "news.example", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);
        DateOnly? cutoff = needsCutoff ? new DateOnly(2026, 6, 1) : null;

        var result = await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "<NEWS.EXAMPLE>",
            decisionKind,
            cutoff));

        Assert.Equal("news.example", result.TargetValue);
        Assert.Equal("list:news.example", result.GroupKey);
        Assert.Equal(decisionKind, result.DecisionKind);
        Assert.Equal(appliesToFuture, result.AppliesToFuture);
        Assert.Equal(needsCutoff, result.CutoffDateUtc is not null);
        Assert.Equal(1, result.MatchingMessageCount);
        await using var restartedContext = await store.Factory.CreateDbContextAsync();
        var persisted = await restartedContext.ClusterDecisions.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(1, persisted.Revision);
        Assert.Equal(ClusterDecisionChangeKind.Created, (await restartedContext.ClusterDecisionAudits.SingleAsync()).ChangeKind);
    }

    [Theory]
    [InlineData(ClusterTargetType.ListId, "<LIST.EXAMPLE>", "list.example", "list:list.example")]
    [InlineData(ClusterTargetType.Sender, " Person@Example.Test ", "person@example.test", "sender:person@example.test")]
    public async Task SetAsync_NormalizesStableListAndSenderTargets(
        ClusterTargetType targetType,
        string requestedTarget,
        string expectedTarget,
        string expectedGroupKey)
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "m1", targetType, expectedTarget, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await CreateService(store).SetAsync(new SetClusterDecisionRequest(
            targetType,
            requestedTarget,
            ClusterDecisionKind.CleanExistingOnly));

        Assert.Equal(expectedTarget, result.TargetValue);
        Assert.Equal(expectedGroupKey, result.GroupKey);
    }

    [Fact]
    public async Task SetAsync_ReplacesDecisionAndRetainsAuditWhileHumanChoiceOutranksRelationshipEvidence()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(
            store,
            "m1",
            ClusterTargetType.Sender,
            "robot@example.test",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            hasRelationship: true);
        var service = CreateService(store);
        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.Sender,
            "robot@example.test",
            ClusterDecisionKind.KeepProtect));

        var replacement = await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.Sender,
            "robot@example.test",
            ClusterDecisionKind.UnwantedExistingAndFuture));
        var dashboard = await new DashboardQueryService(store.Factory).QueryAsync(
            new DashboardRequest(null, "count", "desc", 1, 10));

        Assert.Equal(2, replacement.Revision);
        Assert.Equal(ClusterDecisionKind.UnwantedExistingAndFuture, dashboard.Groups.Single().DecisionKind);
        Assert.Equal(1, dashboard.Groups.Single().RelationshipCount);
        Assert.Equal(1, dashboard.Groups.Single().AffectedMessageCount);
        await using var db = await store.Factory.CreateDbContextAsync();
        var history = await db.ClusterDecisionAudits.OrderBy(audit => audit.Revision).ToListAsync();
        Assert.Equal([ClusterDecisionChangeKind.Created, ClusterDecisionChangeKind.Replaced], history.Select(audit => audit.ChangeKind));
        Assert.Equal([ClusterDecisionKind.KeepProtect, ClusterDecisionKind.UnwantedExistingAndFuture], history.Select(audit => audit.DecisionKind));
    }

    [Fact]
    public async Task Defer_ReviewsSourceWithoutPolicyAndReplacementUpdatesBothStates()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "high-1", ClusterTargetType.ListId, "high.example", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "high-2", ClusterTargetType.ListId, "high.example", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "low-1", ClusterTargetType.ListId, "low.example", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);
        var dashboard = new DashboardQueryService(store.Factory);

        var before = await dashboard.QueryAsync(new DashboardRequest(null, "triage", "desc", 1, 10));
        Assert.Equal("high.example", before.Groups[0].TargetValue);
        Assert.False(before.Groups[0].HasHumanDecision);
        Assert.False(before.Groups[0].HasPolicy);

        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "high.example",
            ClusterDecisionKind.Defer));

        var afterDefer = await dashboard.QueryAsync(new DashboardRequest(null, "triage", "desc", 1, 10));
        var unreviewed = await dashboard.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 10, DecisionState: "unreviewed"));
        var deferred = await dashboard.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 10, DecisionState: "deferred"));
        var policyAfterDefer = await dashboard.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 10, DecisionState: "policy"));

        Assert.Equal("low.example", afterDefer.Groups[0].TargetValue);
        Assert.Single(unreviewed.Groups);
        Assert.Equal("low.example", unreviewed.Groups.Single().TargetValue);
        Assert.Single(deferred.Groups);
        Assert.Equal("high.example", deferred.Groups.Single().TargetValue);
        Assert.True(deferred.Groups.Single().HasHumanDecision);
        Assert.False(deferred.Groups.Single().HasPolicy);
        Assert.Empty(policyAfterDefer.Groups);
        Assert.Equal(1, afterDefer.Metrics.ReviewedGroupCount);
        Assert.Equal(1, afterDefer.Metrics.UnreviewedGroupCount);
        Assert.Equal(0, afterDefer.Metrics.PolicyDecisionCount);
        Assert.Equal(0, afterDefer.Metrics.CoveredMessageCount);

        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "high.example",
            ClusterDecisionKind.KeepProtect));

        var afterPolicy = await dashboard.QueryAsync(new DashboardRequest(null, "triage", "desc", 1, 10));
        var deferredAfterPolicy = await dashboard.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 10, DecisionState: "deferred"));
        var policy = await dashboard.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 10, DecisionState: "policy"));

        Assert.Empty(deferredAfterPolicy.Groups);
        Assert.Single(policy.Groups);
        Assert.True(policy.Groups.Single().HasHumanDecision);
        Assert.True(policy.Groups.Single().HasPolicy);
        Assert.Equal(1, afterPolicy.Metrics.ReviewedGroupCount);
        Assert.Equal(1, afterPolicy.Metrics.PolicyDecisionCount);
        Assert.Equal(2, afterPolicy.Metrics.CoveredMessageCount);
    }

    [Fact]
    public async Task RemoveAsync_DeactivatesDecisionAndPreservesRemovalAudit()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "m1", ClusterTargetType.ListId, "offers.example", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);
        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "offers.example",
            ClusterDecisionKind.CleanExistingOnly));

        var removed = await service.RemoveAsync(ClusterTargetType.ListId, "offers.example");

        Assert.True(removed);
        await using var restartedContext = await store.Factory.CreateDbContextAsync();
        var decision = await restartedContext.ClusterDecisions.SingleAsync();
        Assert.False(decision.IsActive);
        Assert.Equal(2, decision.Revision);
        var history = await restartedContext.ClusterDecisionAudits.OrderBy(audit => audit.Revision).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(ClusterDecisionChangeKind.Removed, history[^1].ChangeKind);
        Assert.False(history[^1].IsActive);
        var dashboard = await new DashboardQueryService(store.Factory).QueryAsync(
            new DashboardRequest(null, "triage", "desc", 1, 10));
        Assert.Equal(0, dashboard.Metrics.PolicyDecisionCount);
        Assert.Equal(0, dashboard.Metrics.CoveredMessageCount);
        Assert.False(dashboard.Groups.Single().HasHumanDecision);
        Assert.False(dashboard.Groups.Single().HasPolicy);
    }

    [Fact]
    public async Task DashboardMetrics_CountCutoffAffectedMessagesAndCoverage()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "age-old", ClusterTargetType.ListId, "age.example", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "age-new", ClusterTargetType.ListId, "age.example", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "keep-1", ClusterTargetType.Sender, "friend@example.test", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "keep-2", ClusterTargetType.Sender, "friend@example.test", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "bad-1", ClusterTargetType.ListId, "bad.example", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "bad-2", ClusterTargetType.ListId, "bad.example", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "bad-3", ClusterTargetType.ListId, "bad.example", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);
        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "age.example",
            ClusterDecisionKind.CleanOlderThan,
            new DateOnly(2026, 6, 1)));
        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.Sender,
            "friend@example.test",
            ClusterDecisionKind.KeepProtect));
        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "bad.example",
            ClusterDecisionKind.UnwantedExistingAndFuture));

        var result = await new DashboardQueryService(store.Factory).QueryAsync(
            new DashboardRequest(null, "triage", "desc", 1, 10));

        Assert.Equal(7, result.Metrics.MessageCount);
        Assert.Equal(3, result.Metrics.PolicyDecisionCount);
        Assert.Equal(3, result.Metrics.ReviewedGroupCount);
        Assert.Equal(0, result.Metrics.UnreviewedGroupCount);
        Assert.Equal(2, result.Metrics.KeptMessageCount);
        Assert.Equal(1, result.Metrics.AgeRuleAffectedMessageCount);
        Assert.Equal(4, result.Metrics.IntendedQuarantineMessageCount);
        Assert.Equal(6, result.Metrics.CoveredMessageCount);
        Assert.Equal(6d / 7d * 100d, result.Metrics.PolicyCoveragePercentage, precision: 6);
    }

    [Theory]
    [InlineData(2026, 7, 20, "30-day shortcut")]
    [InlineData(2026, 5, 21, "custom cutoff")]
    [InlineData(2025, 8, 19, "1-year shortcut")]
    public async Task SetAsync_PreservesExistingExplicitAgeCutoff(
        int year,
        int month,
        int day,
        string origin)
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "m1", ClusterTargetType.ListId, "age.example", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);
        var cutoff = new DateOnly(year, month, day);
        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "age.example",
            ClusterDecisionKind.CleanOlderThan,
            cutoff));

        var preserved = await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "age.example",
            ClusterDecisionKind.CleanOlderThan,
            PreserveExistingCutoff: true));

        var expectedCutoff = cutoff.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        Assert.True(
            preserved.CutoffDateUtc == expectedCutoff,
            $"The {origin} should remain at {expectedCutoff:O} when editing without a replacement.");
        Assert.Equal(2, preserved.Revision);
        await using var db = await store.Factory.CreateDbContextAsync();
        Assert.Equal(
            [preserved.CutoffDateUtc, preserved.CutoffDateUtc],
            await db.ClusterDecisionAudits.OrderBy(audit => audit.Revision)
                .Select(audit => audit.CutoffDateUtc)
                .ToListAsync());
    }

    [Fact]
    public async Task SetAsync_ChangingExplicitCutoffUpdatesAffectedMessageCount()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "old", ClusterTargetType.ListId, "age.example", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "middle", ClusterTargetType.ListId, "age.example", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddMessageAsync(store, "new", ClusterTargetType.ListId, "age.example", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);
        var dashboard = new DashboardQueryService(store.Factory);
        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "age.example",
            ClusterDecisionKind.CleanOlderThan,
            new DateOnly(2026, 7, 1)));
        var initial = await dashboard.QueryAsync(new DashboardRequest(null, "triage", "desc", 1, 10));

        await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "age.example",
            ClusterDecisionKind.CleanOlderThan,
            new DateOnly(2026, 2, 1)));
        var changed = await dashboard.QueryAsync(new DashboardRequest(null, "triage", "desc", 1, 10));

        Assert.Equal(2, initial.Groups.Single().AffectedMessageCount);
        Assert.Equal(1, changed.Groups.Single().AffectedMessageCount);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), changed.Groups.Single().DecisionCutoffDateUtc);
    }

    [Fact]
    public async Task SetAsync_RejectsInvalidCutoffAndPolicyCombinations()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "m1", ClusterTargetType.ListId, "news.example", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);

        await Assert.ThrowsAsync<ClusterDecisionValidationException>(() => service.SetAsync(
            new SetClusterDecisionRequest(ClusterTargetType.ListId, "news.example", ClusterDecisionKind.CleanOlderThan)));
        await Assert.ThrowsAsync<ClusterDecisionValidationException>(() => service.SetAsync(
            new SetClusterDecisionRequest(ClusterTargetType.ListId, "news.example", ClusterDecisionKind.KeepProtect, new DateOnly(2026, 1, 1))));
        await Assert.ThrowsAsync<ClusterDecisionValidationException>(() => service.SetAsync(
            new SetClusterDecisionRequest(ClusterTargetType.ListId, "news.example", ClusterDecisionKind.CleanOlderThan, new DateOnly(2026, 8, 20))));
        await Assert.ThrowsAsync<ClusterDecisionValidationException>(() => service.SetAsync(
            new SetClusterDecisionRequest(
                ClusterTargetType.ListId,
                "news.example",
                ClusterDecisionKind.CleanOlderThan,
                PreserveExistingCutoff: true)));
    }

    [Fact]
    public async Task CleanExistingOnly_ExcludesFutureMailWhileFuturePolicyIncludesIt()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await AddMessageAsync(store, "existing", ClusterTargetType.ListId, "stream.example", new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc));
        var service = CreateService(store);
        var existingOnly = await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "stream.example",
            ClusterDecisionKind.CleanExistingOnly));
        await AddMessageAsync(store, "future", ClusterTargetType.ListId, "stream.example", new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));

        var existingOnlyDashboard = await new DashboardQueryService(store.Factory).QueryAsync(
            new DashboardRequest(null, "count", "desc", 1, 10));
        var futurePolicy = await service.SetAsync(new SetClusterDecisionRequest(
            ClusterTargetType.ListId,
            "stream.example",
            ClusterDecisionKind.UnwantedExistingAndFuture));
        var futureDashboard = await new DashboardQueryService(store.Factory).QueryAsync(
            new DashboardRequest(null, "count", "desc", 1, 10));

        Assert.False(existingOnly.AppliesToFuture);
        Assert.Equal(1, existingOnlyDashboard.Groups.Single().AffectedMessageCount);
        Assert.True(futurePolicy.AppliesToFuture);
        Assert.Equal(2, futureDashboard.Groups.Single().AffectedMessageCount);
    }

    private static ClusterDecisionService CreateService(SqliteTestStore store) =>
        new(store.Factory, new FixedTimeProvider(Now));

    private static async Task AddMessageAsync(
        SqliteTestStore store,
        string id,
        ClusterTargetType targetType,
        string targetValue,
        DateTime dateUtc,
        bool hasRelationship = false)
    {
        var normalizedTarget = ClusterDecisionService.NormalizeTarget(targetType, targetValue);
        var listId = targetType == ClusterTargetType.ListId ? normalizedTarget : null;
        var sender = targetType == ClusterTargetType.Sender ? normalizedTarget : $"sender-{id}@example.test";
        await using var db = await store.Factory.CreateDbContextAsync();
        db.Messages.Add(new MessageRecord
        {
            GmailMessageId = id,
            ThreadId = $"thread-{id}",
            DateUtc = dateUtc,
            LabelIdsJson = "[]",
            SenderName = normalizedTarget,
            SenderAddress = sender,
            NormalizedSenderAddress = sender,
            Subject = $"Subject {id}",
            ListId = listId,
            GroupKey = ClusterDecisionService.GroupKey(targetType, normalizedTarget),
            GroupDisplay = normalizedTarget,
            GroupKind = targetType == ClusterTargetType.ListId ? "List-ID" : "Sender",
            HasDirectCorrespondence = hasRelationship,
            HasThreadInteraction = hasRelationship,
            LastSeenScanId = "test",
            IndexedAtUtc = Now.UtcDateTime
        });
        await db.SaveChangesAsync();
    }
}
