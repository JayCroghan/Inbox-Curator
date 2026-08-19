using System.Text.Json;
using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Services;

public sealed class SyntheticDataSeeder(IDbContextFactory<InboxCuratorDbContext> contextFactory)
{
    private static readonly (string Display, string Address, string? ListId, string[] Subjects, int Count, int PromotionRate)[] Sources =
    [
        ("Signal & Type", "dispatch@signalandtype.example", "dispatch.signalandtype.example", ["The quiet infrastructure issue", "Design systems that age well", "A field note from Kyoto"], 18, 25),
        ("Northstar Market", "hello@northstarmarket.example", "offers.northstarmarket.example", ["A better carry for late summer", "Member preview: field essentials", "Your Wednesday edit"], 14, 90),
        ("Build Log Weekly", "editor@buildlog.example", "weekly.buildlog.example", ["SQLite at the edge", "The long life of boring software", "Tracing the invisible queue"], 11, 15),
        ("Mina Chen", "mina.chen@example.test", null, ["Re: September planning", "Dinner next Thursday?", "Notes from the workshop"], 9, 0),
        ("Atlas Travel", "journeys@atlas.example", "news.atlas.example", ["48 hours in Hangzhou", "Three quiet mountain stays", "Your autumn fare watch"], 8, 60),
        ("Github", "notifications@github.example", null, ["Review requested: indexing checkpoint", "Re: dashboard query cleanup", "Security alert resolved"], 7, 0),
        ("Community Garden", "updates@garden.example", "members.garden.example", ["Saturday planting roster", "August plot notes", "Tool shed update"], 5, 0),
        ("The Daily Ledger", "briefing@ledger.example", "daily.ledger.example", ["Markets find their footing", "Morning brief: five signals", "The week in one chart"], 4, 20)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Messages.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = new DateTime(2026, 8, 19, 3, 0, 0, DateTimeKind.Utc);
        var sequence = 0;
        foreach (var source in Sources)
        {
            for (var index = 0; index < source.Count; index++)
            {
                var listId = AddressNormalizer.NormalizeListId(source.ListId);
                var normalized = AddressNormalizer.NormalizeAddress(source.Address);
                var direct = source.Address.EndsWith("example.test", StringComparison.Ordinal);
                db.Messages.Add(new MessageRecord
                {
                    GmailMessageId = $"synthetic-{sequence:0000}",
                    ThreadId = direct ? $"thread-personal-{index / 2}" : $"thread-{sequence:0000}",
                    DateUtc = now.AddDays(-(sequence * 3 % 190)).AddHours(-(sequence % 20)),
                    LabelIdsJson = JsonSerializer.Serialize(index % 3 == 0 ? new[] { "INBOX", "UNREAD" } : new[] { "INBOX" }),
                    SenderName = source.Display,
                    SenderAddress = source.Address,
                    NormalizedSenderAddress = normalized,
                    ReplyToAddress = source.Address,
                    Subject = source.Subjects[index % source.Subjects.Length],
                    ListId = listId,
                    HasListUnsubscribe = listId is not null,
                    HasAttachment = direct && index % 4 == 0,
                    IsUnread = index % 3 == 0,
                    IsStarred = index % 7 == 0,
                    IsImportant = direct || index % 8 == 0,
                    IsPromotion = (index * 37) % 100 < source.PromotionRate,
                    HasDirectCorrespondence = direct,
                    HasThreadInteraction = direct && index % 2 == 0,
                    GroupKey = listId is null ? $"sender:{normalized}" : $"list:{listId}",
                    GroupDisplay = listId ?? source.Display,
                    GroupKind = listId is null ? "Sender" : "List-ID",
                    LastSeenScanId = "synthetic",
                    IndexedAtUtc = now
                });
                sequence++;
            }
        }

        db.ScanCheckpoints.AddRange(
            new ScanCheckpoint { Kind = ScanKind.Census, State = ScanState.Completed, ProcessedCount = sequence, StartedAtUtc = now.AddMinutes(-3), UpdatedAtUtc = now, CompletedAtUtc = now },
            new ScanCheckpoint { Kind = ScanKind.Sent, State = ScanState.Completed, ProcessedCount = 12, StartedAtUtc = now.AddMinutes(-1), UpdatedAtUtc = now, CompletedAtUtc = now });

        AddDecision(db, ClusterTargetType.ListId, "dispatch.signalandtype.example", ClusterDecisionKind.KeepProtect, null, now);
        AddDecision(db, ClusterTargetType.ListId, "offers.northstarmarket.example", ClusterDecisionKind.UnwantedExistingAndFuture, null, now);
        AddDecision(db, ClusterTargetType.ListId, "news.atlas.example", ClusterDecisionKind.CleanOlderThan, now.AddDays(-90).Date, now);
        AddDecision(db, ClusterTargetType.Sender, "notifications@github.example", ClusterDecisionKind.Defer, null, now);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddDecision(
        InboxCuratorDbContext db,
        ClusterTargetType targetType,
        string targetValue,
        ClusterDecisionKind decisionKind,
        DateTime? cutoffDateUtc,
        DateTime now)
    {
        var normalizedTarget = ClusterDecisionService.NormalizeTarget(targetType, targetValue);
        var groupKey = ClusterDecisionService.GroupKey(targetType, normalizedTarget);
        var matchingMessageCount = db.Messages.Local.Count(message => message.GroupKey == groupKey);
        var decision = new ClusterDecision
        {
            TargetType = targetType,
            TargetValue = normalizedTarget,
            GroupKey = groupKey,
            DecisionKind = decisionKind,
            CutoffDateUtc = cutoffDateUtc,
            AppliesToFuture = ClusterDecisionService.AppliesToFuture(decisionKind),
            IsActive = true,
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        decision.AuditEntries.Add(new ClusterDecisionAudit
        {
            ChangeKind = ClusterDecisionChangeKind.Created,
            DecisionKind = decisionKind,
            CutoffDateUtc = cutoffDateUtc,
            AppliesToFuture = decision.AppliesToFuture,
            IsActive = true,
            Revision = 1,
            MatchingMessageCount = matchingMessageCount,
            ChangedAtUtc = now
        });
        db.ClusterDecisions.Add(decision);
    }
}
