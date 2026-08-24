using InboxCurator.Classification;
using InboxCurator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InboxCurator.Pages;

public sealed class ClassifierModel(
    ClassifierLabService lab,
    EvaluationCorpusService corpora,
    ClassifierPromptService prompts,
    ClassifierRunService runs) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? RunId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? ProfileId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ScoreSort { get; set; } = "profile";

    [BindProperty(SupportsGet = true, Name = "dir")]
    public string Direction { get; set; } = "asc";

    [BindProperty(SupportsGet = true)]
    public int ErrorPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public ClassifierInspectionMode InspectionMode { get; set; } = ClassifierInspectionMode.Disagreements;

    [BindProperty]
    public List<string> SelectedProfiles { get; set; } = [];

    [BindProperty]
    public long CorpusId { get; set; }

    [BindProperty]
    public string PromptVersion { get; set; } = ClassifierPromptV3Definition.Version;

    public ClassifierLabSnapshot Snapshot { get; private set; } = null!;

    [TempData]
    public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Snapshot = await lab.GetAsync(
            RunId,
            ProfileId,
            ScoreSort,
            Direction,
            InspectionMode,
            ErrorPage,
            cancellationToken);

    public static string InspectionLabel(ClassifierInspectionMode mode) => mode switch
    {
        ClassifierInspectionMode.SelfRepaired => "Self-repaired results",
        ClassifierInspectionMode.SemanticWarnings => "Semantic warnings",
        ClassifierInspectionMode.NormalizationWarnings => "Normalization warnings",
        ClassifierInspectionMode.All => "All evaluated results",
        _ => "Disagreements & failures"
    };

    public async Task<IActionResult> OnPostCreateCorpusAsync(CancellationToken cancellationToken)
    {
        var corpus = await corpora.CreateAsync(cancellationToken);
        Notice = $"Frozen corpus {corpus.Version} created with {corpus.EligibleSourceCount:N0} eligible sources.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLockPromptAsync(
        long corpusId,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await prompts.LockAsync(promptVersion, corpusId, cancellationToken);
            Notice = $"{promptVersion} is locked to corpus {corpusId}. Holdout remains disabled in MAIL-003A.1.";
        }
        catch (InvalidOperationException exception)
        {
            Notice = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var runId = await runs.CreateAsync(
                ClassifierRunStage.DevelopmentValidation,
                SelectedProfiles,
                CorpusId,
                PromptVersion,
                cancellationToken);
            Notice = $"Development + validation run queued with {PromptVersion}.";
            return RedirectToPage(new { runId });
        }
        catch (InvalidOperationException exception)
        {
            Notice = exception.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostCancelAsync(string runId, CancellationToken cancellationToken)
    {
        await runs.CancelAsync(runId, cancellationToken);
        Notice = "Cancellation requested. The active inference will stop at the next cancellation boundary.";
        return RedirectToPage(new { runId });
    }

    public string NextDirection(string column) =>
        string.Equals(ScoreSort, column, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

    public static string Percentage(double? value) => value.HasValue ? $"{value.Value:N1}%" : "—";
    public static string Duration(double? milliseconds) => milliseconds.HasValue ? $"{milliseconds.Value / 1000d:N1}s" : "—";
    public static string Elapsed(RunLabSummary run) =>
        ((run.CompletedAtUtc ?? DateTime.UtcNow) - (run.StartedAtUtc ?? run.CreatedAtUtc)).ToString(@"hh\:mm\:ss");
}
