using System.Net;
using InboxCurator.Classification;
using InboxCurator.Data;
using InboxCurator.Gmail;
using InboxCurator.Scanning;
using InboxCurator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContextFactory<InboxCuratorDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("InboxCurator")));
builder.Services.Configure<GmailOptions>(builder.Configuration.GetSection(GmailOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.AddSingleton<IGoogleCredentialProvider, InstalledAppCredentialProvider>();
builder.Services.AddSingleton<IGmailMailboxClient, GmailMailboxClient>();
builder.Services.AddSingleton<IRetryDelay, SystemRetryDelay>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MailboxScanner>();
builder.Services.AddSingleton<ScanCoordinator>();
builder.Services.AddSingleton<IScanTrigger>(services => services.GetRequiredService<ScanCoordinator>());
builder.Services.AddHostedService(services => services.GetRequiredService<ScanCoordinator>());
builder.Services.AddSingleton<IOllamaRetryDelay, SystemOllamaRetryDelay>();
builder.Services.AddHttpClient<IOllamaApiClient, OllamaApiClient>(client =>
    client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<OllamaClusterClassifier>();
builder.Services.AddSingleton<IClusterClassifier>(services => services.GetRequiredService<OllamaClusterClassifier>());
builder.Services.AddSingleton<ILocalModelRuntime>(services => services.GetRequiredService<OllamaClusterClassifier>());
builder.Services.AddSingleton<ClassifierRunExecutor>();
builder.Services.AddSingleton<ClassifierRunCoordinator>();
builder.Services.AddSingleton<IClassifierRunQueue>(services => services.GetRequiredService<ClassifierRunCoordinator>());
builder.Services.AddHostedService(services => services.GetRequiredService<ClassifierRunCoordinator>());
builder.Services.AddScoped<DashboardQueryService>();
builder.Services.AddScoped<ClusterDecisionService>();
builder.Services.AddScoped<ClusterDetailQueryService>();
builder.Services.AddScoped<SyntheticDataSeeder>();
builder.Services.AddScoped<EvaluationCorpusService>();
builder.Services.AddScoped<ClassifierPromptService>();
builder.Services.AddScoped<ClassifierRunService>();
builder.Services.AddScoped<ClassifierLabService>();
builder.Services.AddScoped<SyntheticEvaluationSeeder>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    var port = builder.Configuration.GetValue("LocalPort", 5137);
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
}

var app = builder.Build();

var sqliteConnection = app.Configuration.GetConnectionString("InboxCurator")
    ?? throw new InvalidOperationException("ConnectionStrings:InboxCurator is required.");
var sqliteDataSource = new SqliteConnectionStringBuilder(sqliteConnection).DataSource;
if (!string.IsNullOrWhiteSpace(sqliteDataSource) && sqliteDataSource != ":memory:")
{
    var databasePath = Path.IsPathRooted(sqliteDataSource)
        ? sqliteDataSource
        : Path.GetFullPath(sqliteDataSource, app.Environment.ContentRootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InboxCuratorDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<ClassifierPromptService>().EnsureV1Async();

    if (builder.Configuration.GetValue<bool>("SeedSyntheticData"))
    {
        await scope.ServiceProvider.GetRequiredService<SyntheticDataSeeder>().SeedAsync();
    }

    if (builder.Configuration.GetValue<bool>("SeedSyntheticEvaluationData"))
    {
        await scope.ServiceProvider.GetRequiredService<SyntheticEvaluationSeeder>().SeedAsync();
    }
}

await app.RunAsync();

public partial class Program;
