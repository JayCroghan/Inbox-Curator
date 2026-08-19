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
        Assert.Single(search.Groups);
        Assert.Equal("Mina Chen", search.Groups[0].Display);
        Assert.Equal(9, search.Groups[0].RelationshipCount);
        Assert.NotEmpty(result.Groups[0].RepresentativeSubjects);
    }
}
