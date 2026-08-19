using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Options;

namespace InboxCurator.Gmail;

public interface IGoogleCredentialProvider
{
    Task<UserCredential> AuthorizeAsync(CancellationToken cancellationToken);
}

public sealed class InstalledAppCredentialProvider(
    IOptions<GmailOptions> options,
    IWebHostEnvironment environment) : IGoogleCredentialProvider
{
    public static readonly IReadOnlyList<string> AuthorizedScopes = [GmailService.Scope.GmailReadonly];

    public async Task<UserCredential> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var secretsPath = ResolvePath(environment.ContentRootPath, settings.ClientSecretsPath);
        var tokenPath = ResolvePath(environment.ContentRootPath, settings.TokenStorePath);

        if (!File.Exists(secretsPath))
        {
            throw new FileNotFoundException(
                "Gmail OAuth client secrets were not found. Follow the README setup and place the installed-app JSON at the configured Gmail:ClientSecretsPath.",
                secretsPath);
        }

        await using var stream = new FileStream(secretsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken)).Secrets;
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            AuthorizedScopes,
            "local-user",
            cancellationToken,
            new FileDataStore(tokenPath, true));
    }

    private static string ResolvePath(string contentRoot, string configuredPath) =>
        Path.IsPathRooted(configuredPath) ? configuredPath : Path.GetFullPath(configuredPath, contentRoot);
}
