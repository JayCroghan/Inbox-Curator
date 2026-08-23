namespace InboxCurator.Classification;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";
    public int MaxRetryAttempts { get; set; } = 2;
    public List<OllamaModelProfile> Profiles { get; set; } = [];
}

public sealed class OllamaModelProfile
{
    public required string Key { get; set; }
    public required string Model { get; set; }
    public string? Think { get; set; }
    public double Temperature { get; set; } = 0;
    public int NumCtx { get; set; } = 8192;
    public bool Stream { get; set; }
    public string KeepAlive { get; set; } = "30m";
}
