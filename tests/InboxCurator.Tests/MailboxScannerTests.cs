using InboxCurator.Data;
using InboxCurator.Gmail;
using InboxCurator.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InboxCurator.Tests;

public sealed class MailboxScannerTests
{
    [Fact]
    public async Task ScanAsync_IsIdempotentAndReconcilesSentRelationships()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var gmail = new FakeGmailMailboxClient();
        gmail.Pages[(ScanKind.Sent, null)] = new GmailPage(["s1"], null);
        gmail.Pages[(ScanKind.Census, null)] = new GmailPage(["m1", "m2"], null);
        gmail.Messages["s1"] = Metadata("s1", "thread-known", new Dictionary<string, string> { ["To"] = "Mina <mina@example.test>" }, "SENT");
        gmail.Messages["m1"] = Metadata("m1", "thread-known", new Dictionary<string, string> { ["From"] = "Mina <mina@example.test>", ["Subject"] = "Re: plan" }, "INBOX");
        gmail.Messages["m2"] = Metadata("m2", "thread-news", new Dictionary<string, string> { ["From"] = "News <news@example.test>", ["List-ID"] = "<weekly.example>", ["Subject"] = "Week 32" }, "INBOX");
        var scanner = CreateScanner(gmail, store.Factory);

        await scanner.ScanAsync(CancellationToken.None);
        gmail.Pages[(ScanKind.Census, null)] = new GmailPage(["m1"], null);
        await scanner.ScanAsync(CancellationToken.None);

        await using var db = await store.Factory.CreateDbContextAsync();
        Assert.Single(await db.Messages.ToListAsync());
        Assert.Single(await db.SentInteractions.ToListAsync());
        var personal = await db.Messages.SingleAsync(message => message.GmailMessageId == "m1");
        Assert.True(personal.HasDirectCorrespondence);
        Assert.True(personal.HasThreadInteraction);
        Assert.All(await db.ScanCheckpoints.ToListAsync(), checkpoint => Assert.Equal(ScanState.Completed, checkpoint.State));
    }

    [Fact]
    public async Task FailedScan_ResumesFromLastDurablePageBoundary()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var gmail = new FakeGmailMailboxClient { ThrowOnceForMessageId = "m2" };
        gmail.Pages[(ScanKind.Census, null)] = new GmailPage(["m1"], "page-2");
        gmail.Pages[(ScanKind.Census, "page-2")] = new GmailPage(["m2"], null);
        gmail.Messages["m1"] = Metadata("m1", "t1", new Dictionary<string, string> { ["From"] = "One <one@example.test>" }, "INBOX");
        gmail.Messages["m2"] = Metadata("m2", "t2", new Dictionary<string, string> { ["From"] = "Two <two@example.test>" }, "INBOX");
        var scanner = CreateScanner(gmail, store.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanKindAsync(ScanKind.Census, CancellationToken.None));
        await using (var failedDb = await store.Factory.CreateDbContextAsync())
        {
            var checkpoint = await failedDb.ScanCheckpoints.SingleAsync();
            Assert.Equal(ScanState.Failed, checkpoint.State);
            Assert.Equal("page-2", checkpoint.NextPageToken);
            Assert.Equal(1, await failedDb.Messages.CountAsync());
        }

        await scanner.ScanKindAsync(ScanKind.Census, CancellationToken.None);

        Assert.Equal((ScanKind.Census, "page-2"), gmail.ListRequests[^1]);
        await using var completedDb = await store.Factory.CreateDbContextAsync();
        Assert.Equal(2, await completedDb.Messages.CountAsync());
        Assert.Equal(ScanState.Completed, (await completedDb.ScanCheckpoints.SingleAsync()).State);
    }

    private static MailboxScanner CreateScanner(FakeGmailMailboxClient gmail, IDbContextFactory<InboxCuratorDbContext> factory) =>
        new(gmail, factory, new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero)), NullLogger<MailboxScanner>.Instance);

    private static GmailMessageMetadata Metadata(
        string id,
        string thread,
        IReadOnlyDictionary<string, string> headers,
        params string[] labels) =>
        new(id, thread, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), labels, headers, false);
}
