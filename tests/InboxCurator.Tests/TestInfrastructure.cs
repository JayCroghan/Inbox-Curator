using InboxCurator.Data;
using InboxCurator.Gmail;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Tests;

internal sealed class SqliteTestStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public IDbContextFactory<InboxCuratorDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<InboxCuratorDbContext>().UseSqlite(_connection).Options;
        Factory = new TestDbContextFactory(options);
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private sealed class TestDbContextFactory(DbContextOptions<InboxCuratorDbContext> options)
        : IDbContextFactory<InboxCuratorDbContext>
    {
        public InboxCuratorDbContext CreateDbContext() => new(options);

        public Task<InboxCuratorDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

internal sealed class FakeGmailMailboxClient : IGmailMailboxClient
{
    public Dictionary<(ScanKind Kind, string? Token), GmailPage> Pages { get; } = [];
    public Dictionary<string, GmailMessageMetadata> Messages { get; } = [];
    public List<(ScanKind Kind, string? Token)> ListRequests { get; } = [];
    public string? ThrowOnceForMessageId { get; set; }

    public Task<GmailPage> ListAsync(ScanKind kind, string? pageToken, CancellationToken cancellationToken)
    {
        ListRequests.Add((kind, pageToken));
        return Task.FromResult(Pages[(kind, pageToken)]);
    }

    public Task<GmailMessageMetadata> GetMetadataAsync(string messageId, CancellationToken cancellationToken)
    {
        if (ThrowOnceForMessageId == messageId)
        {
            ThrowOnceForMessageId = null;
            throw new InvalidOperationException("Synthetic one-time API failure.");
        }

        return Task.FromResult(Messages[messageId]);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
}

internal sealed record EmailFixture(IReadOnlyDictionary<string, string> Headers, string Body)
{
    public static async Task<EmailFixture> LoadAsync(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", filename);
        var content = await File.ReadAllTextAsync(path);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var parts = normalized.Split("\n\n", 2, StringSplitOptions.None);
        var headers = parts[0].Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
        return new EmailFixture(headers, parts.Length == 2 ? parts[1] : string.Empty);
    }

    public GmailMessageMetadata ToMetadata(string id, string threadId, params string[] labels) =>
        new(id, threadId, new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc), labels, Headers, false);
}
