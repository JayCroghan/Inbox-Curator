using InboxCurator.Data;
using InboxCurator.Services;

namespace InboxCurator.Tests;

public sealed class ClusterDetailQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_ReturnsPagedLocalMetadataEvidenceAndCurrentDecision()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        await new SyntheticDataSeeder(store.Factory).SeedAsync();
        var service = new ClusterDetailQueryService(store.Factory);

        var result = await service.QueryAsync(new ClusterDetailRequest(
            ClusterTargetType.ListId,
            "<DISPATCH.SIGNALANDTYPE.EXAMPLE>",
            Page: 2,
            PageSize: 10));

        Assert.NotNull(result);
        Assert.Equal("dispatch.signalandtype.example", result.TargetValue);
        Assert.Equal(18, result.Evidence.MessageCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(8, result.Messages.Count);
        Assert.All(result.Messages, message => Assert.StartsWith("synthetic-", message.GmailMessageId, StringComparison.Ordinal));
        Assert.Contains(result.Messages, message => message.IsUnread);
        Assert.Equal(ClusterDecisionKind.KeepProtect, result.Decision?.DecisionKind);
        Assert.Equal(18, result.Decision?.AffectedMessageCount);
    }
}
