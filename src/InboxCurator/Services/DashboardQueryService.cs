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
            .GroupBy(message => new { message.GroupKey, message.GroupDisplay, message.GroupKind })
            .Select(group => new GroupSummary
            {
                GroupKey = group.Key.GroupKey,
                Display = group.Key.GroupDisplay,
                Kind = group.Key.GroupKind,
                MessageCount = group.Count(),
                FirstDateUtc = group.Min(message => message.DateUtc),
                LastDateUtc = group.Max(message => message.DateUtc),
                UnreadCount = group.Count(message => message.IsUnread),
                StarredCount = group.Count(message => message.IsStarred),
                ImportantCount = group.Count(message => message.IsImportant),
                RelationshipCount = group.Count(message => message.HasDirectCorrespondence || message.HasThreadInteraction),
                PromotionCount = group.Count(message => message.IsPromotion)
            });

        var totalGroups = await aggregate.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(request.PageSize, 10, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalGroups / (double)pageSize));
        var page = Math.Clamp(request.Page, 1, totalPages);
        var descending = !string.Equals(request.Direction, "asc", StringComparison.OrdinalIgnoreCase);
        var sorted = ApplySort(aggregate, request.Sort, descending);
        var groups = await sorted.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var keys = groups.Select(group => group.GroupKey).ToArray();
        if (keys.Length > 0)
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

        var metrics = new DashboardMetrics(
            await db.Messages.CountAsync(cancellationToken),
            await db.Messages.CountAsync(message => message.IsUnread, cancellationToken),
            totalGroups,
            await db.Messages.CountAsync(message => message.HasDirectCorrespondence || message.HasThreadInteraction, cancellationToken));

        var checkpoints = await db.ScanCheckpoints.AsNoTracking().OrderBy(item => item.Kind).ToListAsync(cancellationToken);
        return new DashboardResult(groups, metrics, checkpoints, page, pageSize, totalGroups, totalPages);
    }

    private static IOrderedQueryable<GroupSummary> ApplySort(IQueryable<GroupSummary> query, string? sort, bool descending)
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
            (_, false) => query.OrderBy(group => group.MessageCount),
            _ => query.OrderByDescending(group => group.MessageCount)
        };
    }
}

public sealed record DashboardRequest(string? Search, string? Sort, string? Direction, int Page = 1, int PageSize = 25);

public sealed record DashboardResult(
    IReadOnlyList<GroupSummary> Groups,
    DashboardMetrics Metrics,
    IReadOnlyList<ScanCheckpoint> Checkpoints,
    int Page,
    int PageSize,
    int TotalGroups,
    int TotalPages);

public sealed record DashboardMetrics(int MessageCount, int UnreadCount, int GroupCount, int RelationshipCount);

public sealed class GroupSummary
{
    public required string GroupKey { get; init; }
    public required string Display { get; init; }
    public required string Kind { get; init; }
    public int MessageCount { get; init; }
    public DateTime FirstDateUtc { get; init; }
    public DateTime LastDateUtc { get; init; }
    public int UnreadCount { get; init; }
    public int StarredCount { get; init; }
    public int ImportantCount { get; init; }
    public int RelationshipCount { get; init; }
    public int PromotionCount { get; init; }
    public double PromotionPercentage => MessageCount == 0 ? 0 : PromotionCount * 100d / MessageCount;
    public IReadOnlyList<string> RepresentativeSubjects { get; set; } = [];
}
