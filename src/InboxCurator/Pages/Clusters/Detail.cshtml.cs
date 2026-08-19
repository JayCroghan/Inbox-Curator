using InboxCurator.Data;
using InboxCurator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InboxCurator.Pages.Clusters;

public sealed class DetailModel(ClusterDetailQueryService details) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public ClusterTargetType TargetType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string TargetValue { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public ClusterDetailResult Result { get; private set; } = null!;
    public string CurrentUrl => Request.Path + Request.QueryString;
    public string BackUrl => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

    [TempData]
    public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await details.QueryAsync(
                new ClusterDetailRequest(TargetType, TargetValue, PageNumber, PageSize),
                cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            Result = result;
            return Page();
        }
        catch (ClusterDecisionValidationException)
        {
            return NotFound();
        }
    }
}
