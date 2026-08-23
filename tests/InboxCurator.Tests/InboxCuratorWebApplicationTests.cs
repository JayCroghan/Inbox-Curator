using System.Net;
using System.Text.RegularExpressions;
using InboxCurator.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Tests;

public sealed class InboxCuratorWebApplicationTests
{
    [Fact]
    public async Task Dashboard_RendersSyntheticCensusWithSecurityHeadersAndSearch()
    {
        using var factory = new SyntheticInboxCuratorFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        var filtered = await client.GetStringAsync("/?q=Mina&sort=sender&dir=asc&pageSize=10");
        var detail = await client.GetStringAsync("/Clusters/Detail?targetType=ListId&targetValue=dispatch.signalandtype.example&pageSize=10");

        response.EnsureSuccessStatusCode();
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("Mailbox triage", html, StringComparison.Ordinal);
        Assert.Contains("explicit policies cover", html, StringComparison.Ordinal);
        Assert.Contains("Keep / protect", html, StringComparison.Ordinal);
        Assert.Contains("dispatch.signalandtype.example", html, StringComparison.Ordinal);
        Assert.Contains("76", html, StringComparison.Ordinal);
        Assert.Contains("Mina Chen", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("Signal &amp; Type", filtered, StringComparison.Ordinal);
        Assert.Contains("Matching messages", detail, StringComparison.Ordinal);
        Assert.Contains("Local metadata only", detail, StringComparison.Ordinal);
        Assert.Contains("The quiet infrastructure issue", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORE ALL PREVIOUS INSTRUCTIONS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORE ALL PREVIOUS INSTRUCTIONS", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgeRuleEditor_ShowsAndPreservesExactCutoffUntilShortcutIsSelected()
    {
        using var factory = new SyntheticInboxCuratorFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        Assert.Contains("Clean before 21 May 2026", html, StringComparison.Ordinal);
        Assert.Contains("Keep current · 21 May 2026", html, StringComparison.Ordinal);
        Assert.Contains("8 affected today", html, StringComparison.Ordinal);
        var currentOptionIndex = html.IndexOf("Keep current · 21 May 2026", StringComparison.Ordinal);
        var ageSelectStart = html.LastIndexOf("<select", currentOptionIndex, StringComparison.Ordinal);
        var ageSelectEnd = html.IndexOf("</select>", currentOptionIndex, StringComparison.Ordinal);
        var ageSelectMarkup = html[ageSelectStart..(ageSelectEnd + "</select>".Length)];
        Assert.DoesNotMatch("<option value=\"90\"[^>]*selected", ageSelectMarkup);

        html = await PostAgeDecisionAsync(client, html, "existing");

        Assert.Contains("Clean before 21 May 2026", html, StringComparison.Ordinal);
        Assert.Contains("Keep current · 21 May 2026", html, StringComparison.Ordinal);

        html = await PostAgeDecisionAsync(client, html, "365");

        DateTime? storedCutoff;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InboxCuratorDbContext>>();
            await using var db = await contextFactory.CreateDbContextAsync();
            storedCutoff = await db.ClusterDecisions
                .Where(decision => decision.TargetValue == "news.atlas.example")
                .Select(decision => decision.CutoffDateUtc)
                .SingleAsync();
        }
        var notice = Regex.Match(html, "<div class=\"notice\"[^>]*>(.*?)</div>", RegexOptions.CultureInvariant).Groups[1].Value;
        Assert.True(
            storedCutoff == new DateTime(2025, 8, 19, 0, 0, 0, DateTimeKind.Utc),
            $"Expected the 1-year shortcut cutoff, but stored {storedCutoff:O}. Notice: {notice}");
        Assert.Contains("Clean before 19 Aug 2025", html, StringComparison.Ordinal);
        Assert.Contains("0 affected today", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Clean before 21 May 2026", html, StringComparison.Ordinal);

        html = await PostAgeDecisionAsync(client, html, "existing");
        Assert.Contains("Clean before 19 Aug 2025", html, StringComparison.Ordinal);

        html = await PostAgeDecisionAsync(client, html, "custom", "2026-04-15");
        Assert.Contains("Clean before 15 Apr 2026", html, StringComparison.Ordinal);
        Assert.Contains("8 affected today", html, StringComparison.Ordinal);

        html = await PostAgeDecisionAsync(client, html, "existing", "2026-04-15");
        Assert.Contains("Clean before 15 Apr 2026", html, StringComparison.Ordinal);

        html = await PostAgeDecisionAsync(client, html, "30", "2026-04-15");
        Assert.Contains("Clean before 20 Jul 2026", html, StringComparison.Ordinal);

        html = await PostAgeDecisionAsync(client, html, "existing", "2026-07-20");
        Assert.Contains("Clean before 20 Jul 2026", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriageMutationForms_OptIntoScrollPreservationWithoutAffectingOtherForms()
    {
        using var factory = new SyntheticInboxCuratorFactory();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/?decisionState=all&pageNumber=1&pageSize=10");
        var forms = Regex.Matches(html, "<form\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .ToArray();

        var decideForms = forms.Where(form => form.Contains("handler=Decide", StringComparison.Ordinal)).ToArray();
        var removeForms = forms.Where(form => form.Contains("handler=RemoveDecision", StringComparison.Ordinal)).ToArray();
        var unrelatedForms = forms.Except(decideForms).Except(removeForms).ToArray();

        Assert.NotEmpty(decideForms);
        Assert.NotEmpty(removeForms);
        Assert.All(decideForms, form => Assert.Contains("data-preserve-triage-scroll", form, StringComparison.Ordinal));
        Assert.All(removeForms, form => Assert.Contains("data-preserve-triage-scroll", form, StringComparison.Ordinal));
        Assert.Contains(unrelatedForms, form => form.Contains("method=\"get\"", StringComparison.Ordinal));
        Assert.Contains(unrelatedForms, form => form.Contains("handler=Scan", StringComparison.Ordinal));
        Assert.All(unrelatedForms, form => Assert.DoesNotContain("data-preserve-triage-scroll", form, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScrollRestorationScript_ScopesConsumesAndExpiresStoredState()
    {
        using var factory = new SyntheticInboxCuratorFactory();
        using var client = factory.CreateClient();
        var script = await client.GetStringAsync("/js/site.js");

        Assert.Contains("form[data-preserve-triage-scroll]", script, StringComparison.Ordinal);
        Assert.Contains("pathname: target.pathname", script, StringComparison.Ordinal);
        Assert.Contains("search: target.search", script, StringComparison.Ordinal);
        Assert.Contains("state.pathname !== window.location.pathname", script, StringComparison.Ordinal);
        Assert.Contains("state.search !== window.location.search", script, StringComparison.Ordinal);
        Assert.Contains("age < 0 || age > maxScrollStateAgeMs", script, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"pageshow\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("preventDefault", script, StringComparison.Ordinal);

        var readIndex = script.IndexOf("window.sessionStorage.getItem(scrollStateKey)", StringComparison.Ordinal);
        var removeIndex = script.IndexOf("window.sessionStorage.removeItem(scrollStateKey)", readIndex, StringComparison.Ordinal);
        var parseIndex = script.IndexOf("JSON.parse(serializedState)", StringComparison.Ordinal);
        var restoreIndex = script.IndexOf("window.scrollTo", StringComparison.Ordinal);
        Assert.True(readIndex >= 0 && readIndex < removeIndex);
        Assert.True(removeIndex < parseIndex, "Stored state should be consumed before it is parsed or restored.");
        Assert.True(parseIndex < restoreIndex);
    }

    private static async Task<string> PostAgeDecisionAsync(
        HttpClient client,
        string html,
        string cutoffPreset,
        string customCutoff = "2026-05-21")
    {
        var tokenMatch = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(tokenMatch.Success, "The rendered decision form should include an antiforgery token.");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value),
            ["TargetType"] = "ListId",
            ["TargetValue"] = "news.atlas.example",
            ["Decision"] = "CleanOlderThan",
            ["DecisionCutoffPreset"] = cutoffPreset,
            ["CustomCutoff"] = customCutoff,
            ["ReturnUrl"] = "/"
        });
        using var response = await client.PostAsync("/?handler=Decide", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private sealed class SyntheticInboxCuratorFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"inbox-curator-tests-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:InboxCurator"] = $"Data Source={_databasePath}",
                    ["SeedSyntheticData"] = "true",
                    ["Gmail:ScanOnStartup"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(
                    new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            if (disposing && File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }
}
