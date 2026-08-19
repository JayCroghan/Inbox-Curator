namespace InboxCurator.Gmail;

public sealed class GmailOptions
{
    public const string SectionName = "Gmail";
    public const int DefaultMaxConcurrentMessageFetches = 12;
    public const int MinimumMaxConcurrentMessageFetches = 1;
    public const int MaximumMaxConcurrentMessageFetches = 32;

    public string ApplicationName { get; set; } = "InboxCurator";
    public string ClientSecretsPath { get; set; } = "client_secret.json";
    public string TokenStorePath { get; set; } = "tokens";
    public bool ScanOnStartup { get; set; }
    public int PageSize { get; set; } = 250;
    public int MaxRetryAttempts { get; set; } = 6;
    public int MaxConcurrentMessageFetches { get; set; } = DefaultMaxConcurrentMessageFetches;
}
