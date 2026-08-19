using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Services;

public sealed class ClusterDetailQueryService(IDbContextFactory<InboxCuratorDbContext> contextFactory)
{
    public async Task<ClusterDetailResult?> QueryAsync(
        ClusterDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        var targetValue = ClusterDecisionService.NormalizeTarget(request.TargetType, request.TargetValue);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var messages = ClusterDecisionService.MatchingMessages(db, request.TargetType, targetValue).AsNoTracking();
        var messageCount = await messages.CountAsync(cancellationToken);
        if (messageCount == 0)
        {
            return null;
        }

        var pageSize = Math.Clamp(request.PageSize, 10, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(messageCount / (double)pageSize));
        var page = Math.Clamp(request.Page, 1, totalPages);
        var groupKey = ClusterDecisionService.GroupKey(request.TargetType, targetValue);
        var display = await messages.OrderByDescending(message => message.DateUtc)
            .Select(message => message.GroupDisplay)
            .FirstAsync(cancellationToken);
        var summary = await messages.GroupBy(_ => 1).Select(group => new ClusterEvidence(
            messageCount,
            group.Min(message => message.DateUtc),
            group.Max(message => message.DateUtc),
            group.Count(message => message.IsUnread),
            group.Count(message => message.IsStarred),
            group.Count(message => message.IsImportant),
            group.Count(message => message.IsPromotion),
            group.Count(message => message.HasDirectCorrespondence || message.HasThreadInteraction)))
            .SingleAsync(cancellationToken);
        var decision = await db.ClusterDecisions.AsNoTracking().SingleOrDefaultAsync(
            item => item.TargetType == request.TargetType && item.TargetValue == targetValue && item.IsActive,
            cancellationToken);
        var affectedMessageCount = decision is null
            ? 0
            : await CountAffectedMessagesAsync(messages, decision, cancellationToken);
        var pageMessages = await messages.OrderByDescending(message => message.DateUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new ClusterMessageSummary(
                message.GmailMessageId,
                message.DateUtc,
                message.Subject,
                message.IsUnread,
                message.IsStarred,
                message.IsImportant,
                message.IsPromotion,
                message.HasDirectCorrespondence,
                message.HasThreadInteraction))
            .ToListAsync(cancellationToken);

        return new ClusterDetailResult(
            request.TargetType,
            targetValue,
            groupKey,
            display,
            summary,
            decision is null
                ? null
                : new ClusterDecisionView(
                    decision.DecisionKind,
                    decision.CutoffDateUtc,
                    decision.AppliesToFuture,
                    decision.UpdatedAtUtc,
                    decision.Revision,
                    affectedMessageCount),
            pageMessages,
            page,
            pageSize,
            totalPages);
    }

    private static Task<int> CountAffectedMessagesAsync(
        IQueryable<MessageRecord> messages,
        ClusterDecision decision,
        CancellationToken cancellationToken) =>
        decision.DecisionKind switch
        {
            ClusterDecisionKind.KeepProtect or
            ClusterDecisionKind.UnwantedExistingAndFuture => messages.CountAsync(cancellationToken),
            ClusterDecisionKind.CleanExistingOnly =>
                messages.CountAsync(message => message.DateUtc <= decision.UpdatedAtUtc, cancellationToken),
            ClusterDecisionKind.CleanOlderThan when decision.CutoffDateUtc is not null =>
                messages.CountAsync(message => message.DateUtc < decision.CutoffDateUtc, cancellationToken),
            _ => Task.FromResult(0)
        };
}

public sealed record ClusterDetailRequest(
    ClusterTargetType TargetType,
    string TargetValue,
    int Page = 1,
    int PageSize = 25);

public sealed record ClusterDetailResult(
    ClusterTargetType TargetType,
    string TargetValue,
    string GroupKey,
    string Display,
    ClusterEvidence Evidence,
    ClusterDecisionView? Decision,
    IReadOnlyList<ClusterMessageSummary> Messages,
    int Page,
    int PageSize,
    int TotalPages);

public sealed record ClusterEvidence(
    int MessageCount,
    DateTime FirstDateUtc,
    DateTime LastDateUtc,
    int UnreadCount,
    int StarredCount,
    int ImportantCount,
    int PromotionCount,
    int RelationshipCount)
{
    public double PromotionPercentage => MessageCount == 0 ? 0 : PromotionCount * 100d / MessageCount;
}

public sealed record ClusterDecisionView(
    ClusterDecisionKind DecisionKind,
    DateTime? CutoffDateUtc,
    bool AppliesToFuture,
    DateTime UpdatedAtUtc,
    int Revision,
    int AffectedMessageCount);

public sealed record ClusterMessageSummary(
    string GmailMessageId,
    DateTime DateUtc,
    string Subject,
    bool IsUnread,
    bool IsStarred,
    bool IsImportant,
    bool IsPromotion,
    bool HasDirectCorrespondence,
    bool HasThreadInteraction);
