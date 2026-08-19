using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Services;

public sealed class ClusterDecisionService(
    IDbContextFactory<InboxCuratorDbContext> contextFactory,
    TimeProvider timeProvider)
{
    public async Task<ClusterDecisionSnapshot> SetAsync(
        SetClusterDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.DecisionKind))
        {
            throw new ClusterDecisionValidationException("The selected decision type is invalid.");
        }

        var targetValue = NormalizeTarget(request.TargetType, request.TargetValue);
        var cutoffDateUtc = ValidateAndConvertCutoff(request.DecisionKind, request.CutoffDate);
        var appliesToFuture = AppliesToFuture(request.DecisionKind);
        var groupKey = GroupKey(request.TargetType, targetValue);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var matchingMessageCount = await MatchingMessages(db, request.TargetType, targetValue)
            .CountAsync(cancellationToken);
        if (matchingMessageCount == 0)
        {
            throw new ClusterDecisionValidationException("The selected sender or List-ID no longer has any indexed messages.");
        }

        var decision = await db.ClusterDecisions
            .SingleOrDefaultAsync(
                item => item.TargetType == request.TargetType && item.TargetValue == targetValue,
                cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changeKind = decision is null ? ClusterDecisionChangeKind.Created : ClusterDecisionChangeKind.Replaced;

        if (decision is null)
        {
            decision = new ClusterDecision
            {
                TargetType = request.TargetType,
                TargetValue = targetValue,
                GroupKey = groupKey,
                DecisionKind = request.DecisionKind,
                CutoffDateUtc = cutoffDateUtc,
                AppliesToFuture = appliesToFuture,
                IsActive = true,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.ClusterDecisions.Add(decision);
        }
        else
        {
            decision.GroupKey = groupKey;
            decision.DecisionKind = request.DecisionKind;
            decision.CutoffDateUtc = cutoffDateUtc;
            decision.AppliesToFuture = appliesToFuture;
            decision.IsActive = true;
            decision.Revision++;
            decision.UpdatedAtUtc = now;
        }

        decision.AuditEntries.Add(CreateAudit(decision, changeKind, matchingMessageCount, now));
        await db.SaveChangesAsync(cancellationToken);
        return ToSnapshot(decision, matchingMessageCount);
    }

    public async Task<bool> RemoveAsync(
        ClusterTargetType targetType,
        string targetValue,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = NormalizeTarget(targetType, targetValue);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var decision = await db.ClusterDecisions.SingleOrDefaultAsync(
            item => item.TargetType == targetType && item.TargetValue == normalizedTarget && item.IsActive,
            cancellationToken);
        if (decision is null)
        {
            return false;
        }

        var matchingMessageCount = await MatchingMessages(db, targetType, normalizedTarget)
            .CountAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        decision.IsActive = false;
        decision.Revision++;
        decision.UpdatedAtUtc = now;
        decision.AuditEntries.Add(CreateAudit(
            decision,
            ClusterDecisionChangeKind.Removed,
            matchingMessageCount,
            now));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static string NormalizeTarget(ClusterTargetType targetType, string targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            throw new ClusterDecisionValidationException("A sender or List-ID target is required.");
        }

        var normalized = targetType switch
        {
            ClusterTargetType.ListId => AddressNormalizer.NormalizeListId(targetValue),
            ClusterTargetType.Sender => AddressNormalizer.NormalizeAddress(targetValue),
            _ => null
        };

        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ClusterDecisionValidationException("The sender or List-ID target is invalid.")
            : normalized;
    }

    public static string GroupKey(ClusterTargetType targetType, string normalizedTarget) =>
        targetType == ClusterTargetType.ListId ? $"list:{normalizedTarget}" : $"sender:{normalizedTarget}";

    public static IQueryable<MessageRecord> MatchingMessages(
        InboxCuratorDbContext db,
        ClusterTargetType targetType,
        string normalizedTarget) =>
        targetType == ClusterTargetType.ListId
            ? db.Messages.Where(message => message.ListId == normalizedTarget)
            : db.Messages.Where(message => message.ListId == null && message.NormalizedSenderAddress == normalizedTarget);

    public static bool AppliesToFuture(ClusterDecisionKind decisionKind) => decisionKind is
        ClusterDecisionKind.KeepProtect or ClusterDecisionKind.UnwantedExistingAndFuture;

    private DateTime? ValidateAndConvertCutoff(ClusterDecisionKind decisionKind, DateOnly? cutoffDate)
    {
        if (decisionKind == ClusterDecisionKind.CleanOlderThan)
        {
            if (cutoffDate is null)
            {
                throw new ClusterDecisionValidationException("Clean older than requires a cutoff date.");
            }

            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            if (cutoffDate > today)
            {
                throw new ClusterDecisionValidationException("The cutoff date cannot be in the future.");
            }

            return DateTime.SpecifyKind(cutoffDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }

        if (cutoffDate is not null)
        {
            throw new ClusterDecisionValidationException("A cutoff date is valid only for clean-older-than decisions.");
        }

        return null;
    }

    private static ClusterDecisionAudit CreateAudit(
        ClusterDecision decision,
        ClusterDecisionChangeKind changeKind,
        int matchingMessageCount,
        DateTime changedAtUtc) =>
        new()
        {
            ChangeKind = changeKind,
            DecisionKind = decision.DecisionKind,
            CutoffDateUtc = decision.CutoffDateUtc,
            AppliesToFuture = decision.AppliesToFuture,
            IsActive = decision.IsActive,
            Revision = decision.Revision,
            MatchingMessageCount = matchingMessageCount,
            ChangedAtUtc = changedAtUtc
        };

    private static ClusterDecisionSnapshot ToSnapshot(ClusterDecision decision, int matchingMessageCount) =>
        new(
            decision.Id,
            decision.TargetType,
            decision.TargetValue,
            decision.GroupKey,
            decision.DecisionKind,
            decision.CutoffDateUtc,
            decision.AppliesToFuture,
            decision.IsActive,
            decision.Revision,
            decision.CreatedAtUtc,
            decision.UpdatedAtUtc,
            matchingMessageCount);
}

public sealed record SetClusterDecisionRequest(
    ClusterTargetType TargetType,
    string TargetValue,
    ClusterDecisionKind DecisionKind,
    DateOnly? CutoffDate = null);

public sealed record ClusterDecisionSnapshot(
    long Id,
    ClusterTargetType TargetType,
    string TargetValue,
    string GroupKey,
    ClusterDecisionKind DecisionKind,
    DateTime? CutoffDateUtc,
    bool AppliesToFuture,
    bool IsActive,
    int Revision,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int MatchingMessageCount);

public sealed class ClusterDecisionValidationException(string message) : InvalidOperationException(message);

public static class ClusterDecisionPresentation
{
    public static string TargetLabel(ClusterTargetType targetType) =>
        targetType == ClusterTargetType.ListId ? "List-ID" : "Sender";

    public static string Label(ClusterDecisionKind decisionKind) => decisionKind switch
    {
        ClusterDecisionKind.KeepProtect => "Keep / protect",
        ClusterDecisionKind.UnwantedExistingAndFuture => "Unwanted · existing + future",
        ClusterDecisionKind.CleanExistingOnly => "Clean existing only",
        ClusterDecisionKind.CleanOlderThan => "Clean older than",
        ClusterDecisionKind.Defer => "Deferred",
        _ => "Undecided"
    };

    public static string CssClass(ClusterDecisionKind? decisionKind) => decisionKind switch
    {
        ClusterDecisionKind.KeepProtect => "is-keep",
        ClusterDecisionKind.UnwantedExistingAndFuture => "is-unwanted",
        ClusterDecisionKind.CleanExistingOnly => "is-clean",
        ClusterDecisionKind.CleanOlderThan => "is-age",
        ClusterDecisionKind.Defer => "is-deferred",
        _ => "is-undecided"
    };
}
