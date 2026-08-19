using InboxCurator.Data;
using InboxCurator.Scanning;
using InboxCurator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InboxCurator.Pages;

public sealed class IndexModel(
    DashboardQueryService dashboard,
    ClusterDecisionService decisions,
    IScanTrigger scanTrigger,
    TimeProvider timeProvider) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "triage";

    [BindProperty(SupportsGet = true, Name = "dir")]
    public string Direction { get; set; } = "desc";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    [BindProperty(SupportsGet = true)]
    public string DecisionState { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string Relationship { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string Promotion { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string StalePreset { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public DateTime? LastBefore { get; set; }

    [BindProperty]
    public ClusterTargetType TargetType { get; set; }

    [BindProperty]
    public string TargetValue { get; set; } = string.Empty;

    [BindProperty]
    public ClusterDecisionKind Decision { get; set; }

    [BindProperty]
    public string? DecisionCutoffPreset { get; set; }

    [BindProperty]
    public DateTime? CustomCutoff { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public DashboardResult Result { get; private set; } = null!;
    public bool ScanIsRunning => scanTrigger.IsRunning;
    public string CurrentUrl => Request.Path + Request.QueryString;

    [TempData]
    public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await dashboard.QueryAsync(
            new DashboardRequest(
                Search,
                Sort,
                Direction,
                PageNumber,
                PageSize,
                DecisionState,
                Relationship,
                Promotion,
                Kind,
                ResolveStaleCutoff()),
            cancellationToken);
    }

    public IActionResult OnPostScan()
    {
        var accepted = scanTrigger.RequestScan();
        Notice = accepted ? "Scan queued. OAuth will open if authorization is required." : "A scan is already queued.";
        return RedirectBack();
    }

    public async Task<IActionResult> OnPostDecideAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notice = "The selected decision could not be understood.";
            return RedirectBack();
        }

        try
        {
            DateOnly? cutoff = Decision == ClusterDecisionKind.CleanOlderThan ? ResolveDecisionCutoff() : null;
            var saved = await decisions.SetAsync(
                new SetClusterDecisionRequest(TargetType, TargetValue, Decision, cutoff),
                cancellationToken);
            Notice = saved.DecisionKind == ClusterDecisionKind.Defer
                ? "Source deferred. It remains outside policy coverage."
                : $"Saved {ClusterDecisionPresentation.Label(saved.DecisionKind).ToLowerInvariant()} for {saved.MatchingMessageCount:N0} matching messages.";
        }
        catch (ClusterDecisionValidationException exception)
        {
            Notice = exception.Message;
        }

        return RedirectBack();
    }

    public async Task<IActionResult> OnPostRemoveDecisionAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notice = "The selected decision target could not be understood.";
            return RedirectBack();
        }

        try
        {
            var removed = await decisions.RemoveAsync(TargetType, TargetValue, cancellationToken);
            Notice = removed ? "Decision removed. The source is undecided again." : "No active decision was found.";
        }
        catch (ClusterDecisionValidationException exception)
        {
            Notice = exception.Message;
        }

        return RedirectBack();
    }

    public string NextDirection(string column) =>
        string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";

    public string SortIndicator(string column) =>
        !string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : string.Equals(Direction, "asc", StringComparison.OrdinalIgnoreCase) ? "↑" : "↓";

    public Dictionary<string, string> RouteValues(
        string? sort = null,
        string? direction = null,
        int? pageNumber = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sort"] = sort ?? Sort,
            ["dir"] = direction ?? Direction,
            ["pageNumber"] = (pageNumber ?? PageNumber).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pageSize"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["decisionState"] = DecisionState,
            ["relationship"] = Relationship,
            ["promotion"] = Promotion,
            ["kind"] = Kind,
            ["stalePreset"] = StalePreset
        };
        if (!string.IsNullOrWhiteSpace(Search))
        {
            values["q"] = Search;
        }

        if (LastBefore is not null)
        {
            values["lastBefore"] = LastBefore.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        return values;
    }

    private DateOnly? ResolveStaleCutoff()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return StalePreset.ToLowerInvariant() switch
        {
            "30" => today.AddDays(-30),
            "90" => today.AddDays(-90),
            "365" => today.AddYears(-1),
            "custom" when LastBefore is not null => DateOnly.FromDateTime(LastBefore.Value),
            _ => null
        };
    }

    private DateOnly ResolveDecisionCutoff()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return DecisionCutoffPreset?.ToLowerInvariant() switch
        {
            "30" => today.AddDays(-30),
            "90" => today.AddDays(-90),
            "365" => today.AddYears(-1),
            "custom" when CustomCutoff is not null => DateOnly.FromDateTime(CustomCutoff.Value),
            _ => throw new ClusterDecisionValidationException("Choose a cutoff preset or custom date.")
        };
    }

    private IActionResult RedirectBack() =>
        !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : RedirectToPage();
}
