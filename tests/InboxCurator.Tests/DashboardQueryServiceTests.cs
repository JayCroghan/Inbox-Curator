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
        Assert.Equal(5, result.Metrics.UndecidedGroupCount);
        Assert.Equal(40, result.Metrics.CoveredMessageCount);
        Assert.Equal(3, result.Metrics.DecisionCount);
        Assert.Single(search.Groups);
        Assert.Equal("Mina Chen", search.Groups[0].Display);
        Assert.Equal(9, search.Groups[0].RelationshipCount);
        Assert.NotEmpty(result.Groups[0].RepresentativeSubjects);
    }

    [Fact]
    public async Task QueryAsync_FiltersDecidedUndecidedRelationshipPromotionKindAndStaleSources()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await new SyntheticDataSeeder(store.Factory).SeedAsync();
        var service = new DashboardQueryService(store.Factory);

        var decided = await service.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 25, DecisionState: "decided"));
        var undecided = await service.QueryAsync(new DashboardRequest(
            null, "triage", "desc", 1, 25, DecisionState: "undecided"));
        var relationship = await service.QueryAsync(new DashboardRequest(
            null, "count", "desc", 1, 25, Relationship: "known"));
        var promotions = await service.QueryAsync(new DashboardRequest(
            null, "count", "desc", 1, 25, Promotion: "promotions"));
        var senders = await service.QueryAsync(new DashboardRequest(
            null, "count", "desc", 1, 25, Kind: "sender"));
        var stale = await service.QueryAsync(new DashboardRequest(
            null, "last", "desc", 1, 25, LastReceivedBefore: new DateOnly(2026, 8, 1)));

        Assert.Equal(3, decided.TotalGroups);
        Assert.All(decided.Groups, group => Assert.True(group.IsDecided));
        Assert.Equal(5, undecided.TotalGroups);
        Assert.All(undecided.Groups, group => Assert.False(group.IsDecided));
        Assert.Contains(undecided.Groups, group => group.DecisionKind == ClusterDecisionKind.Defer);
        Assert.Single(relationship.Groups);
        Assert.All(promotions.Groups, group => Assert.True(group.PromotionCount > 0));
        Assert.Equal(2, senders.TotalGroups);
        Assert.All(senders.Groups, group => Assert.Equal(ClusterTargetType.Sender, group.TargetType));
        Assert.All(stale.Groups, group => Assert.True(group.LastDateUtc < new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}
