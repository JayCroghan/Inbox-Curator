using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Services;

public sealed class DashboardQueryService(IDbContextFactory<InboxCuratorDbContext> contextFactory)
{
    public async Task<DashboardResult> QueryAsync(DashboardRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var source = db.Messages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            source = source.Where(message =>
                message.GroupDisplay.Contains(term) ||
                message.GroupKey.Contains(term) ||
                message.Subject.Contains(term) ||
                message.SenderAddress.Contains(term));
        }

        var aggregate = source
            .GroupBy(message => new { message.GroupKey, message.GroupKind })
            .Select(group => new
            {
                group.Key.GroupKey,
                Display = group.Max(message => message.GroupDisplay),
                Kind = group.Key.GroupKind,
                TargetValue = group.Max(message => message.ListId) ?? group.Max(message => message.NormalizedSenderAddress),
                TargetType = group.Max(message => message.ListId) != null
                    ? ClusterTargetType.ListId
                    : ClusterTargetType.Sender,
                MessageCount = group.Count(),
                FirstDateUtc = group.Min(message => message.DateUtc),
                LastDateUtc = group.Max(message => message.DateUtc),
                UnreadCount = group.Count(message => message.IsUnread),
                StarredCount = group.Count(message => message.IsStarred),
                ImportantCount = group.Count(message => message.IsImportant),
                RelationshipCount = group.Count(message => message.HasDirectCorrespondence || message.HasThreadInteraction),
                PromotionCount = group.Count(message => message.IsPromotion)
            });

        var activeDecisions = db.ClusterDecisions.AsNoTracking().Where(decision => decision.IsActive);
        var groupsWithDecisions =
            from summary in aggregate
            join decision in activeDecisions on summary.GroupKey equals decision.GroupKey into decisions
            from decision in decisions.DefaultIfEmpty()
            select new GroupSummary
            {
                GroupKey = summary.GroupKey,
                Display = summary.Display,
                Kind = summary.Kind,
                TargetType = summary.TargetType,
                TargetValue = summary.TargetValue,
                MessageCount = summary.MessageCount,
                FirstDateUtc = summary.FirstDateUtc,
                LastDateUtc = summary.LastDateUtc,
                UnreadCount = summary.UnreadCount,
                StarredCount = summary.StarredCount,
                ImportantCount = summary.ImportantCount,
                RelationshipCount = summary.RelationshipCount,
                PromotionCount = summary.PromotionCount,
                DecisionKind = decision == null ? null : decision.DecisionKind,
                DecisionCutoffDateUtc = decision == null ? null : decision.CutoffDateUtc,
                AppliesToFuture = decision != null && decision.AppliesToFuture,
                HasHumanDecision = decision != null,
                HasPolicy = decision != null && decision.DecisionKind != ClusterDecisionKind.Defer
            };

        groupsWithDecisions = ApplyFilters(groupsWithDecisions, request);

        var filteredGroupCount = await groupsWithDecisions.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(request.PageSize, 10, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredGroupCount / (double)pageSize));
        var page = Math.Clamp(request.Page, 1, totalPages);
        var descending = !string.Equals(request.Direction, "asc", StringComparison.OrdinalIgnoreCase);
        var sorted = ApplySort(groupsWithDecisions, request.Sort, descending);
        var groups = await sorted.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var keys = groups.Select(group => group.GroupKey).ToArray();
        if (keys.Length > 0)
        {
            await PopulateRepresentativeSubjectsAsync(source, groups, keys, cancellationToken);
            await PopulateAffectedCountsAsync(db, groups, keys, cancellationToken);
        }

        var metrics = await QueryMetricsAsync(db, cancellationToken);
        var checkpoints = await db.ScanCheckpoints.AsNoTracking().OrderBy(item => item.Kind).ToListAsync(cancellationToken);
        return new DashboardResult(groups, metrics, checkpoints, page, pageSize, filteredGroupCount, totalPages);
    }

    private static IQueryable<GroupSummary> ApplyFilters(IQueryable<GroupSummary> query, DashboardRequest request)
    {
        query = request.DecisionState?.ToLowerInvariant() switch
        {
            "unreviewed" or "undecided" => query.Where(group => !group.HasHumanDecision),
            "policy" or "decided" => query.Where(group => group.HasPolicy),
            "deferred" => query.Where(group => group.DecisionKind == ClusterDecisionKind.Defer),
            _ => query
        };

        query = request.Relationship?.ToLowerInvariant() switch
        {
            "known" => query.Where(group => group.RelationshipCount > 0),
            "none" => query.Where(group => group.RelationshipCount == 0),
            _ => query
        };

        query = request.Promotion?.ToLowerInvariant() switch
        {
            "promotions" => query.Where(group => group.PromotionCount > 0),
            "non-promotions" => query.Where(group => group.PromotionCount == 0),
            _ => query
        };

        query = request.Kind?.ToLowerInvariant() switch
        {
            "list" => query.Where(group => group.TargetType == ClusterTargetType.ListId),
            "sender" => query.Where(group => group.TargetType == ClusterTargetType.Sender),
            _ => query
        };

        if (request.LastReceivedBefore is not null)
        {
            var cutoff = DateTime.SpecifyKind(
                request.LastReceivedBefore.Value.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Utc);
            query = query.Where(group => group.LastDateUtc < cutoff);
        }

        return query;
    }

    private static async Task PopulateRepresentativeSubjectsAsync(
        IQueryable<MessageRecord> source,
        IReadOnlyCollection<GroupSummary> groups,
        string[] keys,
        CancellationToken cancellationToken)
    {
        var subjects = await source
            .Where(message => keys.Contains(message.GroupKey) && message.Subject != "")
            .OrderByDescending(message => message.DateUtc)
            .Select(message => new { message.GroupKey, message.Subject })
            .ToListAsync(cancellationToken);

        var representatives = subjects
            .GroupBy(item => item.GroupKey)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.Subject).Distinct().Take(3).ToArray());

        foreach (var group in groups)
        {
            group.RepresentativeSubjects = representatives.GetValueOrDefault(group.GroupKey) ?? [];
        }
    }

    private static async Task PopulateAffectedCountsAsync(
        InboxCuratorDbContext db,
        IReadOnlyCollection<GroupSummary> groups,
        string[] keys,
        CancellationToken cancellationToken)
    {
        var affectedCounts = await (
            from message in db.Messages.AsNoTracking()
            join decision in db.ClusterDecisions.AsNoTracking().Where(item => item.IsActive)
                on message.GroupKey equals decision.GroupKey
            where keys.Contains(message.GroupKey)
            group new { message, decision } by message.GroupKey
            into matches
            select new
            {
                GroupKey = matches.Key,
                Count = matches.Count(match =>
                    match.decision.DecisionKind == ClusterDecisionKind.KeepProtect ||
                    match.decision.DecisionKind == ClusterDecisionKind.UnwantedExistingAndFuture ||
                    (match.decision.DecisionKind == ClusterDecisionKind.CleanExistingOnly &&
                     match.message.DateUtc <= match.decision.UpdatedAtUtc) ||
                    (match.decision.DecisionKind == ClusterDecisionKind.CleanOlderThan &&
                     match.decision.CutoffDateUtc != null &&
                     match.message.DateUtc < match.decision.CutoffDateUtc))
            }).ToDictionaryAsync(item => item.GroupKey, item => item.Count, cancellationToken);

        foreach (var group in groups)
        {
            group.AffectedMessageCount = affectedCounts.GetValueOrDefault(group.GroupKey);
        }
    }

    private static async Task<DashboardMetrics> QueryMetricsAsync(
        InboxCuratorDbContext db,
        CancellationToken cancellationToken)
    {
        var totalMessages = await db.Messages.CountAsync(cancellationToken);
        var totalGroups = await db.Messages.Select(message => message.GroupKey).Distinct().CountAsync(cancellationToken);
        var relationshipMessages = await db.Messages.CountAsync(
            message => message.HasDirectCorrespondence || message.HasThreadInteraction,
            cancellationToken);
        var activeHumanDecisions = db.ClusterDecisions.AsNoTracking().Where(decision => decision.IsActive);
        var activePolicyDecisions = activeHumanDecisions.Where(
            decision => decision.DecisionKind != ClusterDecisionKind.Defer);
        var reviewedGroups = await (
            from groupKey in db.Messages.Select(message => message.GroupKey).Distinct()
            join decision in activeHumanDecisions on groupKey equals decision.GroupKey
            select groupKey).CountAsync(cancellationToken);
        var policyGroups = await (
            from groupKey in db.Messages.Select(message => message.GroupKey).Distinct()
            join decision in activePolicyDecisions on groupKey equals decision.GroupKey
            select groupKey).CountAsync(cancellationToken);

        var matchedMessages =
            from message in db.Messages.AsNoTracking()
            join decision in activePolicyDecisions on message.GroupKey equals decision.GroupKey
            select new { message, decision };

        var keptMessages = await matchedMessages.CountAsync(
            match => match.decision.DecisionKind == ClusterDecisionKind.KeepProtect,
            cancellationToken);
        var ageRuleMessages = await matchedMessages.CountAsync(
            match => match.decision.DecisionKind == ClusterDecisionKind.CleanOlderThan &&
                     match.decision.CutoffDateUtc != null &&
                     match.message.DateUtc < match.decision.CutoffDateUtc,
            cancellationToken);
        var intendedQuarantineMessages = await matchedMessages.CountAsync(
            match =>
                match.decision.DecisionKind == ClusterDecisionKind.UnwantedExistingAndFuture ||
                (match.decision.DecisionKind == ClusterDecisionKind.CleanExistingOnly &&
                 match.message.DateUtc <= match.decision.UpdatedAtUtc) ||
                (match.decision.DecisionKind == ClusterDecisionKind.CleanOlderThan &&
                 match.decision.CutoffDateUtc != null &&
                 match.message.DateUtc < match.decision.CutoffDateUtc),
            cancellationToken);
        var coveredMessages = keptMessages + intendedQuarantineMessages;

        return new DashboardMetrics(
            totalMessages,
            totalGroups,
            relationshipMessages,
            totalGroups - reviewedGroups,
            reviewedGroups,
            keptMessages,
            intendedQuarantineMessages,
            ageRuleMessages,
            coveredMessages,
            policyGroups);
    }

    private static IOrderedQueryable<GroupSummary> ApplySort(
        IQueryable<GroupSummary> query,
        string? sort,
        bool descending)
    {
        return (sort?.ToLowerInvariant(), descending) switch
        {
            ("sender", false) => query.OrderBy(group => group.Display),
            ("sender", true) => query.OrderByDescending(group => group.Display),
            ("first", false) => query.OrderBy(group => group.FirstDateUtc),
            ("first", true) => query.OrderByDescending(group => group.FirstDateUtc),
            ("last", false) => query.OrderBy(group => group.LastDateUtc),
            ("last", true) => query.OrderByDescending(group => group.LastDateUtc),
            ("unread", false) => query.OrderBy(group => group.UnreadCount),
            ("unread", true) => query.OrderByDescending(group => group.UnreadCount),
            ("starred", false) => query.OrderBy(group => group.StarredCount),
            ("starred", true) => query.OrderByDescending(group => group.StarredCount),
            ("important", false) => query.OrderBy(group => group.ImportantCount),
            ("important", true) => query.OrderByDescending(group => group.ImportantCount),
            ("relationship", false) => query.OrderBy(group => group.RelationshipCount),
            ("relationship", true) => query.OrderByDescending(group => group.RelationshipCount),
            ("promotion", false) => query.OrderBy(group => group.PromotionCount * 1.0 / group.MessageCount),
            ("promotion", true) => query.OrderByDescending(group => group.PromotionCount * 1.0 / group.MessageCount),
            ("count", false) => query.OrderBy(group => group.MessageCount),
            ("count", true) => query.OrderByDescending(group => group.MessageCount),
            _ => query.OrderBy(group => group.HasHumanDecision).ThenByDescending(group => group.MessageCount)
        };
    }
}

public sealed record DashboardRequest(
    string? Search,
    string? Sort,
    string? Direction,
    int Page = 1,
    int PageSize = 25,
    string? DecisionState = null,
    string? Relationship = null,
    string? Promotion = null,
    string? Kind = null,
    DateOnly? LastReceivedBefore = null);

public sealed record DashboardResult(
    IReadOnlyList<GroupSummary> Groups,
    DashboardMetrics Metrics,
    IReadOnlyList<ScanCheckpoint> Checkpoints,
    int Page,
    int PageSize,
    int TotalGroups,
    int TotalPages);

public sealed record DashboardMetrics(
    int MessageCount,
    int GroupCount,
    int RelationshipCount,
    int UnreviewedGroupCount,
    int ReviewedGroupCount,
    int KeptMessageCount,
    int IntendedQuarantineMessageCount,
    int AgeRuleAffectedMessageCount,
    int CoveredMessageCount,
    int PolicyDecisionCount)
{
    public double ReviewPercentage => GroupCount == 0 ? 0 : ReviewedGroupCount * 100d / GroupCount;
    public double PolicyCoveragePercentage => MessageCount == 0 ? 0 : CoveredMessageCount * 100d / MessageCount;
}

public sealed class GroupSummary
{
    public required string GroupKey { get; init; }
    public required string Display { get; init; }
    public required string Kind { get; init; }
    public ClusterTargetType TargetType { get; init; }
    public required string TargetValue { get; init; }
    public int MessageCount { get; init; }
    public DateTime FirstDateUtc { get; init; }
    public DateTime LastDateUtc { get; init; }
    public int UnreadCount { get; init; }
    public int StarredCount { get; init; }
    public int ImportantCount { get; init; }
    public int RelationshipCount { get; init; }
    public int PromotionCount { get; init; }
    public ClusterDecisionKind? DecisionKind { get; init; }
    public DateTime? DecisionCutoffDateUtc { get; init; }
    public bool AppliesToFuture { get; init; }
    public bool HasHumanDecision { get; init; }
    public bool HasPolicy { get; init; }
    public int AffectedMessageCount { get; set; }
    public double PromotionPercentage => MessageCount == 0 ? 0 : PromotionCount * 100d / MessageCount;
    public IReadOnlyList<string> RepresentativeSubjects { get; set; } = [];
}
