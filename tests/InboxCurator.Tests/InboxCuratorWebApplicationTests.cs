using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

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

        response.EnsureSuccessStatusCode();
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("Where your inbox", html, StringComparison.Ordinal);
        Assert.Contains("dispatch.signalandtype.example", html, StringComparison.Ordinal);
        Assert.Contains("76", html, StringComparison.Ordinal);
        Assert.Contains("Mina Chen", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("Signal &amp; Type", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORE ALL PREVIOUS INSTRUCTIONS", html, StringComparison.Ordinal);
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
