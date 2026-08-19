using InboxCurator.Data;
using InboxCurator.Gmail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Runtime.ExceptionServices;

namespace InboxCurator.Scanning;

public sealed class MailboxScanner(
    IGmailMailboxClient gmail,
    IDbContextFactory<InboxCuratorDbContext> contextFactory,
    TimeProvider timeProvider,
    IOptions<GmailOptions> gmailOptions,
    ILogger<MailboxScanner> logger)
{
    private readonly int _maxConcurrentMessageFetches = Math.Clamp(
        gmailOptions.Value.MaxConcurrentMessageFetches,
        GmailOptions.MinimumMaxConcurrentMessageFetches,
        GmailOptions.MaximumMaxConcurrentMessageFetches);

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        await ScanKindAsync(ScanKind.Sent, cancellationToken);
        await ScanKindAsync(ScanKind.Census, cancellationToken);
        await ReconcileRelationshipsAsync(cancellationToken);
    }

    public async Task ScanKindAsync(ScanKind kind, CancellationToken cancellationToken)
    {
        var checkpoint = await StartOrResumeAsync(kind, cancellationToken);
        var pageToken = checkpoint.NextPageToken;
        var scanRunId = checkpoint.ActiveRunId
            ?? throw new InvalidOperationException("The scan checkpoint has no active run identifier.");

        try
        {
            do
            {
                var page = await gmail.ListAsync(kind, pageToken, cancellationToken);
                var metadata = await FetchPageMetadataAsync(page.MessageIds, cancellationToken);

                await PersistPageAsync(kind, metadata, page.NextPageToken, scanRunId, cancellationToken);
                pageToken = page.NextPageToken;
            }
            while (pageToken is not null);

            await CompleteAsync(kind, scanRunId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await FailAsync(kind, SanitizeFailureCode(exception), CancellationToken.None);
            throw;
        }
    }

    private async Task<IReadOnlyList<GmailMessageMetadata>> FetchPageMetadataAsync(
        IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        var metadata = new GmailMessageMetadata[messageIds.Count];
        var nextIndex = -1;
        ExceptionDispatchInfo? firstFailure = null;
        using var pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task FetchAsync()
        {
            while (!pageCancellation.IsCancellationRequested)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= messageIds.Count)
                {
                    return;
                }

                try
                {
                    metadata[index] = await gmail.GetMetadataAsync(messageIds[index], pageCancellation.Token);
                }
                catch (OperationCanceledException) when (pageCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    var captured = ExceptionDispatchInfo.Capture(exception);
                    if (Interlocked.CompareExchange(ref firstFailure, captured, null) is null)
                    {
                        await pageCancellation.CancelAsync();
                    }

                    return;
                }
            }
        }

        var workerCount = Math.Min(_maxConcurrentMessageFetches, messageIds.Count);
        var workers = Enumerable.Range(0, workerCount).Select(_ => FetchAsync()).ToArray();
        await Task.WhenAll(workers);

        cancellationToken.ThrowIfCancellationRequested();
        firstFailure?.Throw();
        return metadata;
    }

    private async Task<ScanCheckpoint> StartOrResumeAsync(ScanKind kind, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var checkpoint = await db.ScanCheckpoints.SingleOrDefaultAsync(item => item.Kind == kind, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (checkpoint is null)
        {
            checkpoint = new ScanCheckpoint
            {
                Kind = kind,
                State = ScanState.Running,
                StartedAtUtc = now,
                UpdatedAtUtc = now,
                ActiveRunId = Guid.NewGuid().ToString("N")
            };
            db.ScanCheckpoints.Add(checkpoint);
        }
        else
        {
            if (checkpoint.State == ScanState.Completed)
            {
                checkpoint.NextPageToken = null;
                checkpoint.ProcessedCount = 0;
                checkpoint.StartedAtUtc = now;
                checkpoint.ActiveRunId = Guid.NewGuid().ToString("N");
            }

            checkpoint.State = ScanState.Running;
            checkpoint.UpdatedAtUtc = now;
            checkpoint.CompletedAtUtc = null;
            checkpoint.FailureCode = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("{ScanKind} scan started or resumed at the last durable page boundary.", kind);
        return checkpoint;
    }

    private async Task PersistPageAsync(
        ScanKind kind,
        IReadOnlyCollection<GmailMessageMetadata> metadata,
        string? nextPageToken,
        string scanRunId,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (kind == ScanKind.Census)
        {
            var ids = metadata.Select(item => item.MessageId).ToArray();
            var existing = await db.Messages.Where(item => ids.Contains(item.GmailMessageId))
                .ToDictionaryAsync(item => item.GmailMessageId, cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;

            foreach (var item in metadata)
            {
                var incoming = MessageMetadataMapper.ToRecord(item, now, scanRunId);
                if (existing.TryGetValue(item.MessageId, out var current))
                {
                    CopyMetadata(incoming, current);
                }
                else
                {
                    db.Messages.Add(incoming);
                }
            }
        }
        else
        {
            var interactions = metadata.SelectMany(item => MessageMetadataMapper.ToSentInteractions(item, scanRunId)).ToArray();
            var messageIds = interactions.Select(item => item.GmailMessageId).Distinct().ToArray();
            var existingInteractions = await db.SentInteractions
                .Where(item => messageIds.Contains(item.GmailMessageId))
                .ToDictionaryAsync(item => item.GmailMessageId + "\n" + item.RecipientAddress, cancellationToken);

            foreach (var interaction in interactions)
            {
                var key = interaction.GmailMessageId + "\n" + interaction.RecipientAddress;
                if (existingInteractions.TryGetValue(key, out var existing))
                {
                    existing.ThreadId = interaction.ThreadId;
                    existing.DateUtc = interaction.DateUtc;
                    existing.LastSeenScanId = scanRunId;
                }
                else
                {
                    db.SentInteractions.Add(interaction);
                }
            }
        }

        var checkpoint = await db.ScanCheckpoints.SingleAsync(item => item.Kind == kind, cancellationToken);
        checkpoint.NextPageToken = nextPageToken;
        checkpoint.ProcessedCount += metadata.Count;
        checkpoint.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReconcileRelationshipsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var addresses = await db.SentInteractions.Select(item => item.RecipientAddress).Distinct().ToListAsync(cancellationToken);
        var threads = await db.SentInteractions.Select(item => item.ThreadId).Distinct().ToListAsync(cancellationToken);
        await db.Messages.ExecuteUpdateAsync(setters => setters
            .SetProperty(message => message.HasDirectCorrespondence, message => addresses.Contains(message.NormalizedSenderAddress))
            .SetProperty(message => message.HasThreadInteraction, message => threads.Contains(message.ThreadId)), cancellationToken);
    }

    private async Task CompleteAsync(ScanKind kind, string scanRunId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var checkpoint = await db.ScanCheckpoints.SingleAsync(item => item.Kind == kind, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (kind == ScanKind.Census)
        {
            await db.Messages.Where(message => message.LastSeenScanId != scanRunId).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await db.SentInteractions.Where(interaction => interaction.LastSeenScanId != scanRunId).ExecuteDeleteAsync(cancellationToken);
        }

        checkpoint.State = ScanState.Completed;
        checkpoint.NextPageToken = null;
        checkpoint.UpdatedAtUtc = now;
        checkpoint.CompletedAtUtc = now;
        checkpoint.FailureCode = null;
        checkpoint.ActiveRunId = null;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("{ScanKind} scan completed; {Count} metadata records examined.", kind, checkpoint.ProcessedCount);
    }

    private async Task FailAsync(ScanKind kind, string failureCode, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var checkpoint = await db.ScanCheckpoints.SingleAsync(item => item.Kind == kind, cancellationToken);
        checkpoint.State = ScanState.Failed;
        checkpoint.FailureCode = failureCode;
        checkpoint.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogError("{ScanKind} scan stopped with failure code {FailureCode}; the durable checkpoint was retained.", kind, failureCode);
    }

    private static string SanitizeFailureCode(Exception exception) => exception switch
    {
        FileNotFoundException => "oauth-client-secrets-not-found",
        Google.GoogleApiException google => $"gmail-api-{(int)google.HttpStatusCode}",
        _ => exception.GetType().Name
    };

    private static void CopyMetadata(MessageRecord source, MessageRecord target)
    {
        target.ThreadId = source.ThreadId;
        target.DateUtc = source.DateUtc;
        target.LabelIdsJson = source.LabelIdsJson;
        target.SenderName = source.SenderName;
        target.SenderAddress = source.SenderAddress;
        target.NormalizedSenderAddress = source.NormalizedSenderAddress;
        target.ReplyToAddress = source.ReplyToAddress;
        target.Subject = source.Subject;
        target.ListId = source.ListId;
        target.HasListUnsubscribe = source.HasListUnsubscribe;
        target.HasAttachment = source.HasAttachment;
        target.IsUnread = source.IsUnread;
        target.IsStarred = source.IsStarred;
        target.IsImportant = source.IsImportant;
        target.IsPromotion = source.IsPromotion;
        target.GroupKey = source.GroupKey;
        target.GroupDisplay = source.GroupDisplay;
        target.GroupKind = source.GroupKind;
        target.LastSeenScanId = source.LastSeenScanId;
        target.IndexedAtUtc = source.IndexedAtUtc;
    }
}
