using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Classification;

public sealed class EvaluationCorpusService(
    IDbContextFactory<InboxCuratorDbContext> contextFactory,
    TimeProvider timeProvider)
{
    public const string SplitStrategy = "stratified-sha256-v1;seed=MAIL-003A-SPLIT-SEED-20260823;ratio=60/20/20";

    public async Task<EvaluationCorpus> CreateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var activeDecisions = await db.ClusterDecisions
            .AsNoTracking()
            .Where(decision => decision.IsActive)
            .OrderBy(decision => decision.TargetType)
            .ThenBy(decision => decision.TargetValue)
            .ToListAsync(cancellationToken);

        var eligibleDecisions = activeDecisions
            .Where(decision => decision.DecisionKind is
                ClusterDecisionKind.KeepProtect or ClusterDecisionKind.UnwantedExistingAndFuture)
            .ToList();
        var eligibleGroupKeys = eligibleDecisions.Select(decision => decision.GroupKey).Distinct().ToArray();
        var messages = await db.Messages
            .AsNoTracking()
            .Where(message => eligibleGroupKeys.Contains(message.GroupKey))
            .Select(message => new CorpusMessage(
                message.GmailMessageId,
                message.GroupKey,
                message.GroupDisplay,
                message.DateUtc,
                message.Subject,
                message.IsUnread,
                message.IsStarred,
                message.IsImportant,
                message.HasDirectCorrespondence || message.HasThreadInteraction,
                message.IsPromotion,
                message.HasListUnsubscribe,
                message.HasAttachment))
            .ToListAsync(cancellationToken);
        var messagesByGroup = messages.ToLookup(message => message.GroupKey, StringComparer.Ordinal);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var corpus = new EvaluationCorpus
        {
            Version = $"MAIL-003A-CORPUS-{now:yyyyMMdd-HHmmss-fff}",
            CreatedAtUtc = now,
            SplitStrategy = SplitStrategy,
            ExcludedCleanExistingOnlyCount = activeDecisions.Count(decision => decision.DecisionKind == ClusterDecisionKind.CleanExistingOnly),
            ExcludedCleanOlderThanCount = activeDecisions.Count(decision => decision.DecisionKind == ClusterDecisionKind.CleanOlderThan),
            ExcludedDeferCount = activeDecisions.Count(decision => decision.DecisionKind == ClusterDecisionKind.Defer)
        };

        foreach (var decision in eligibleDecisions)
        {
            var groupMessages = messagesByGroup[decision.GroupKey]
                .OrderBy(message => message.DateUtc)
                .ThenBy(message => message.GmailMessageId, StringComparer.Ordinal)
                .ToArray();
            if (groupMessages.Length == 0)
            {
                corpus.ExcludedMissingEvidenceCount++;
                continue;
            }

            var display = groupMessages
                .OrderByDescending(message => message.DateUtc)
                .ThenBy(message => message.GmailMessageId, StringComparer.Ordinal)
                .Select(message => message.GroupDisplay)
                .First();
            corpus.Items.Add(new EvaluationCorpusItem
            {
                TargetType = decision.TargetType,
                TargetValue = decision.TargetValue,
                GroupKey = decision.GroupKey,
                DisplayName = display,
                GroundTruth = decision.DecisionKind == ClusterDecisionKind.KeepProtect
                    ? EvaluationGroundTruth.Keep
                    : EvaluationGroundTruth.Unwanted,
                MessageCount = groupMessages.Length,
                FirstReceivedUtc = groupMessages[0].DateUtc,
                LastReceivedUtc = groupMessages[^1].DateUtc,
                UnreadCount = groupMessages.Count(message => message.IsUnread),
                StarredCount = groupMessages.Count(message => message.IsStarred),
                ImportantCount = groupMessages.Count(message => message.IsImportant),
                RelationshipCount = groupMessages.Count(message => message.HasRelationship),
                PromotionsCount = groupMessages.Count(message => message.IsPromotion),
                ListUnsubscribeCount = groupMessages.Count(message => message.HasListUnsubscribe),
                AttachmentCount = groupMessages.Count(message => message.HasAttachment),
                RepresentativeSubjectsJson = JsonSerializer.Serialize(
                    RepresentativeSubjectSampler.Sample(groupMessages.Select(message =>
                        new SubjectEvidence(message.GmailMessageId, message.DateUtc, message.Subject))))
            });
        }

        CorpusSplitAssigner.Assign(corpus.Items);
        corpus.EligibleSourceCount = corpus.Items.Count;
        db.EvaluationCorpora.Add(corpus);
        await db.SaveChangesAsync(cancellationToken);
        return corpus;
    }

    private sealed record CorpusMessage(
        string GmailMessageId,
        string GroupKey,
        string GroupDisplay,
        DateTime DateUtc,
        string Subject,
        bool IsUnread,
        bool IsStarred,
        bool IsImportant,
        bool HasRelationship,
        bool IsPromotion,
        bool HasListUnsubscribe,
        bool HasAttachment);
}

public sealed record SubjectEvidence(string StableId, DateTime DateUtc, string Subject);

public static class RepresentativeSubjectSampler
{
    public const int MaximumSubjects = 8;

    public static IReadOnlyList<string> Sample(IEnumerable<SubjectEvidence> messages)
    {
        var ordered = messages
            .OrderBy(message => message.DateUtc)
            .ThenBy(message => message.StableId, StringComparer.Ordinal)
            .ToArray();
        var nonEmpty = ordered
            .Where(message => !string.IsNullOrWhiteSpace(message.Subject))
            .Select(message => message with { Subject = message.Subject.Trim() })
            .GroupBy(message => message.Subject, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(message => message.DateUtc)
            .ThenBy(message => message.StableId, StringComparer.Ordinal)
            .ToArray();

        if (nonEmpty.Length == 0)
        {
            return ordered.Length == 0 ? [] : [string.Empty];
        }

        if (nonEmpty.Length <= MaximumSubjects)
        {
            return nonEmpty.Select(message => message.Subject).ToArray();
        }

        var sampled = new string[MaximumSubjects];
        for (var slot = 0; slot < MaximumSubjects; slot++)
        {
            var index = (int)Math.Round(
                slot * (nonEmpty.Length - 1d) / (MaximumSubjects - 1d),
                MidpointRounding.AwayFromZero);
            sampled[slot] = nonEmpty[index].Subject;
        }

        return sampled;
    }
}

public static class CorpusSplitAssigner
{
    private const string Seed = "MAIL-003A-SPLIT-SEED-20260823";

    public static void Assign(IEnumerable<EvaluationCorpusItem> items)
    {
        foreach (var stratum in items.GroupBy(item => item.GroundTruth))
        {
            var ordered = stratum
                .OrderBy(item => StableHash(item), StringComparer.Ordinal)
                .ThenBy(item => item.TargetType)
                .ThenBy(item => item.TargetValue, StringComparer.Ordinal)
                .ToArray();
            var counts = SplitCounts(ordered.Length);
            for (var index = 0; index < ordered.Length; index++)
            {
                ordered[index].Split = index < counts.Development
                    ? EvaluationSplit.Development
                    : index < counts.Development + counts.Validation
                        ? EvaluationSplit.Validation
                        : EvaluationSplit.Holdout;
            }
        }
    }

    private static (int Development, int Validation, int Holdout) SplitCounts(int count)
    {
        if (count <= 0)
        {
            return (0, 0, 0);
        }

        if (count == 1)
        {
            return (1, 0, 0);
        }

        if (count == 2)
        {
            return (1, 0, 1);
        }

        var development = Math.Max(1, (int)Math.Round(count * .6, MidpointRounding.AwayFromZero));
        var validation = Math.Max(1, (int)Math.Round(count * .2, MidpointRounding.AwayFromZero));
        var holdout = count - development - validation;
        while (holdout < 1 && development > 1)
        {
            development--;
            holdout++;
        }

        while (development + validation + holdout > count && development > 1)
        {
            development--;
        }

        return (development, validation, count - development - validation);
    }

    private static string StableHash(EvaluationCorpusItem item)
    {
        var value = $"{Seed}|{item.GroundTruth}|{item.TargetType}|{item.TargetValue}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
