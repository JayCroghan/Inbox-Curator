namespace InboxCurator.Gmail;

public sealed class GmailOptions
{
    public const string SectionName = "Gmail";

    public string ApplicationName { get; set; } = "InboxCurator";
    public string ClientSecretsPath { get; set; } = "client_secret.json";
    public string TokenStorePath { get; set; } = "tokens";
    public bool ScanOnStartup { get; set; }
    public int PageSize { get; set; } = 250;
    public int MaxRetryAttempts { get; set; } = 6;
}
