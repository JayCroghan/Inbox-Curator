using InboxCurator.Data;
using InboxCurator.Services;

namespace InboxCurator.Tests;

public sealed class DashboardQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_AggregatesSortsSearchesAndPaginatesSyntheticSources()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await new SyntheticDataSeeder(store.Factory).SeedAsync();
        var service = new DashboardQueryService(store.Factory);

        var result = await service.QueryAsync(new DashboardRequest(null, "count", "desc", 1, 10));
        var search = await service.QueryAsync(new DashboardRequest("Mina", "sender", "asc", 1, 10));

        Assert.Equal(76, result.Metrics.MessageCount);
        Assert.Equal(8, result.TotalGroups);
        Assert.Equal(18, result.Groups[0].MessageCount);
        Assert.Equal(4, result.Metrics.UnreviewedGroupCount);
        Assert.Equal(4, result.Metrics.ReviewedGroupCount);
        Assert.Equal(40, result.Metrics.CoveredMessageCount);
        Assert.Equal(3, result.Metrics.PolicyDecisionCount);
        Assert.Single(search.Groups);
        Assert.Equal("Mina Chen", search.Groups[0].Display);
        Assert.Equal(9, search.Groups[0].RelationshipCount);
        Assert.NotEmpty(result.Groups[0].RepresentativeSubjects);
    }

    [Fact]
    public async Task QueryAsync_FiltersPolicyUnreviewedDeferredRelationshipPromotionKindAndStaleSources()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await new SyntheticDataSeeder(store.Factory).SeedAsync();
        var service = new DashboardQueryService(store.Factory);

        var policy = await service.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 25, DecisionState: "policy"));
        var unreviewed = await service.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 25, DecisionState: "unreviewed"));
        var deferred = await service.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 25, DecisionState: "deferred"));
        var relationship = await service.QueryAsync(new DashboardRequest(
            null, "count", "desc", 1, 25, Relationship: "known"));
        var promotions = await service.QueryAsync(new DashboardRequest(
            null, "count", "desc", 1, 25, Promotion: "promotions"));
        var senders = await service.QueryAsync(new DashboardRequest(
            null, "count", "desc", 1, 25, Kind: "sender"));
        var stale = await service.QueryAsync(new DashboardRequest(
            null, "last", "desc", 1, 25, LastReceivedBefore: new DateOnly(2026, 8, 1)));

        Assert.Equal(3, policy.TotalGroups);
        Assert.All(policy.Groups, group => Assert.True(group.HasHumanDecision && group.HasPolicy));
        Assert.Equal(4, unreviewed.TotalGroups);
        Assert.All(unreviewed.Groups, group => Assert.False(group.HasHumanDecision || group.HasPolicy));
        Assert.Single(deferred.Groups);
        Assert.Equal(ClusterDecisionKind.Defer, deferred.Groups.Single().DecisionKind);
        Assert.True(deferred.Groups.Single().HasHumanDecision);
        Assert.False(deferred.Groups.Single().HasPolicy);
        Assert.Single(relationship.Groups);
        Assert.All(promotions.Groups, group => Assert.True(group.PromotionCount > 0));
        Assert.Equal(2, senders.TotalGroups);
        Assert.All(senders.Groups, group => Assert.Equal(ClusterTargetType.Sender, group.TargetType));
        Assert.All(stale.Groups, group => Assert.True(group.LastDateUtc < new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}
