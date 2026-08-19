using InboxCurator.Scanning;
using InboxCurator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InboxCurator.Pages;

public sealed class IndexModel(DashboardQueryService dashboard, IScanTrigger scanTrigger) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "count";

    [BindProperty(SupportsGet = true, Name = "dir")]
    public string Direction { get; set; } = "desc";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public DashboardResult Result { get; private set; } = null!;
    public bool ScanIsRunning => scanTrigger.IsRunning;

    [TempData]
    public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await dashboard.QueryAsync(
            new DashboardRequest(Search, Sort, Direction, PageNumber, PageSize),
            cancellationToken);
    }

    public IActionResult OnPostScan()
    {
        var accepted = scanTrigger.RequestScan();
        Notice = accepted ? "Scan queued. OAuth will open if authorization is required." : "A scan is already queued.";
        return RedirectToPage(new { q = Search, sort = Sort, dir = Direction, pageNumber = PageNumber, pageSize = PageSize });
    }

    public string NextDirection(string column) =>
        string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase) && string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";

    public string SortIndicator(string column) =>
        !string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : string.Equals(Direction, "asc", StringComparison.OrdinalIgnoreCase) ? "↑" : "↓";
}
