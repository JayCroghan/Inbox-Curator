using InboxCurator.Data;

namespace InboxCurator.Pages;

public sealed record DecisionControlsModel(
    ClusterTargetType TargetType,
    string TargetValue,
    ClusterDecisionKind? DecisionKind,
    DateTime? CutoffDateUtc,
    int AffectedMessageCount,
    string ReturnUrl);
