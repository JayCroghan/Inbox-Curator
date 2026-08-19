using InboxCurator.Data;
using InboxCurator.Gmail;
using InboxCurator.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

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

    [Theory]
    [InlineData(3, 3, 12)]
    [InlineData(100, GmailOptions.MaximumMaxConcurrentMessageFetches, 40)]
    public async Task ScanKindAsync_FetchesConcurrentlyWithoutExceedingEffectiveBound(
        int configuredConcurrency,
        int effectiveConcurrency,
        int messageCount)
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var gmail = new BlockingGmailMailboxClient(messageCount, effectiveConcurrency);
        var scanner = CreateScanner(gmail, store.Factory, configuredConcurrency);

        var scan = scanner.ScanKindAsync(ScanKind.Census, CancellationToken.None);
        try
        {
            await gmail.ConcurrencyBoundReached.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            gmail.ReleaseFetches();
        }

        await scan;

        Assert.True(gmail.MaximumConcurrentFetches > 1);
        Assert.True(gmail.MaximumConcurrentFetches <= effectiveConcurrency);
        await using var db = await store.Factory.CreateDbContextAsync();
        Assert.Equal(messageCount, await db.Messages.CountAsync());
    }

    [Fact]
    public async Task ConcurrentFetchFailure_DoesNotPersistOrAdvanceFailedPage()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var gmail = new ConcurrentPageFailureGmailMailboxClient();
        var scanner = CreateScanner(gmail, store.Factory, maxConcurrentMessageFetches: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scanner.ScanKindAsync(ScanKind.Census, CancellationToken.None));

        await using (var failedDb = await store.Factory.CreateDbContextAsync())
        {
            var checkpoint = await failedDb.ScanCheckpoints.SingleAsync();
            Assert.Equal(ScanState.Failed, checkpoint.State);
            Assert.Equal("page-2", checkpoint.NextPageToken);
            Assert.Equal("InvalidOperationException", checkpoint.FailureCode);
            Assert.Equal(1, checkpoint.ProcessedCount);
            Assert.Equal(new[] { "m1" }, await failedDb.Messages.Select(message => message.GmailMessageId).ToArrayAsync());
        }

        await scanner.ScanKindAsync(ScanKind.Census, CancellationToken.None);

        Assert.Equal((ScanKind.Census, "page-2"), gmail.ListRequests[2]);
        Assert.All(new[] { "m2", "m3", "m4" }, messageId => Assert.Equal(2, gmail.FetchCounts[messageId]));

        await scanner.ScanKindAsync(ScanKind.Census, CancellationToken.None);

        await using var completedDb = await store.Factory.CreateDbContextAsync();
        Assert.Equal(4, await completedDb.Messages.CountAsync());
        Assert.Equal(ScanState.Completed, (await completedDb.ScanCheckpoints.SingleAsync()).State);
    }

    private static MailboxScanner CreateScanner(
        IGmailMailboxClient gmail,
        IDbContextFactory<InboxCuratorDbContext> factory,
        int maxConcurrentMessageFetches = GmailOptions.DefaultMaxConcurrentMessageFetches) =>
        new(
            gmail,
            factory,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero)),
            Options.Create(new GmailOptions { MaxConcurrentMessageFetches = maxConcurrentMessageFetches }),
            NullLogger<MailboxScanner>.Instance);

    private static GmailMessageMetadata Metadata(
        string id,
        string thread,
        IReadOnlyDictionary<string, string> headers,
        params string[] labels) =>
        new(id, thread, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), labels, headers, false);

    private sealed class BlockingGmailMailboxClient : IGmailMailboxClient
    {
        private readonly IReadOnlyList<string> _messageIds;
        private readonly int _expectedConcurrency;
        private readonly TaskCompletionSource _concurrencyBoundReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFetches = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeFetches;
        private int _maximumConcurrentFetches;

        public BlockingGmailMailboxClient(int messageCount, int expectedConcurrency)
        {
            _messageIds = Enumerable.Range(1, messageCount).Select(index => $"m{index}").ToArray();
            _expectedConcurrency = expectedConcurrency;
        }

        public Task ConcurrencyBoundReached => _concurrencyBoundReached.Task;

        public int MaximumConcurrentFetches => Volatile.Read(ref _maximumConcurrentFetches);

        public Task<GmailPage> ListAsync(ScanKind kind, string? pageToken, CancellationToken cancellationToken) =>
            Task.FromResult(new GmailPage(_messageIds, null));

        public async Task<GmailMessageMetadata> GetMetadataAsync(string messageId, CancellationToken cancellationToken)
        {
            var activeFetches = Interlocked.Increment(ref _activeFetches);
            UpdateMaximum(activeFetches);
            if (activeFetches >= _expectedConcurrency)
            {
                _concurrencyBoundReached.TrySetResult();
            }

            try
            {
                await _releaseFetches.Task.WaitAsync(cancellationToken);
                return Metadata(
                    messageId,
                    $"thread-{messageId}",
                    new Dictionary<string, string> { ["From"] = $"{messageId} <{messageId}@example.test>" },
                    "INBOX");
            }
            finally
            {
                Interlocked.Decrement(ref _activeFetches);
            }
        }

        public void ReleaseFetches() => _releaseFetches.TrySetResult();

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maximumConcurrentFetches);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref _maximumConcurrentFetches, candidate, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class ConcurrentPageFailureGmailMailboxClient : IGmailMailboxClient
    {
        private readonly TaskCompletionSource _failedPageFetchesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _failedPageStartedCount;

        public ConcurrentDictionary<string, int> FetchCounts { get; } = new(StringComparer.Ordinal);
        public List<(ScanKind Kind, string? Token)> ListRequests { get; } = [];

        public Task<GmailPage> ListAsync(ScanKind kind, string? pageToken, CancellationToken cancellationToken)
        {
            ListRequests.Add((kind, pageToken));
            return Task.FromResult(pageToken switch
            {
                null => new GmailPage(["m1"], "page-2"),
                "page-2" => new GmailPage(["m2", "m3", "m4"], null),
                _ => throw new InvalidOperationException("Unexpected synthetic page token.")
            });
        }

        public async Task<GmailMessageMetadata> GetMetadataAsync(string messageId, CancellationToken cancellationToken)
        {
            var fetchCount = FetchCounts.AddOrUpdate(messageId, 1, (_, current) => current + 1);
            if (messageId != "m1" && fetchCount == 1)
            {
                if (Interlocked.Increment(ref _failedPageStartedCount) == 3)
                {
                    _failedPageFetchesStarted.TrySetResult();
                }

                await _failedPageFetchesStarted.Task.WaitAsync(cancellationToken);
                if (messageId == "m3")
                {
                    throw new InvalidOperationException("Synthetic concurrent page failure.");
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Metadata(
                messageId,
                $"thread-{messageId}",
                new Dictionary<string, string> { ["From"] = $"{messageId} <{messageId}@example.test>" },
                "INBOX");
        }
    }
}
