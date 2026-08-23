using System.Net;
using System.Text;
using System.Text.Json;
using InboxCurator.Classification;
using InboxCurator.Data;
using Microsoft.Extensions.Options;

namespace InboxCurator.Tests;

public sealed class OllamaClientTests
{
    [Fact]
    public async Task Client_ChecksInstalledModelsWithoutPullingAndCapturesResidencyAndUnload()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/tags" => Json("""{"models":[{"name":"qwen3.6:35b","model":"qwen3.6:35b"}]}"""),
            "/api/ps" => Json("""{"models":[{"name":"qwen3.6:35b","model":"qwen3.6:35b","size":24000000000,"size_vram":18000000000,"context_length":8192}]}"""),
            "/api/generate" => Json("""{"done":true}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var installed = await client.GetInstalledModelsAsync(CancellationToken.None);
        var residency = await client.GetResidencyAsync("qwen3.6:35b", CancellationToken.None);
        await client.UnloadAsync("qwen3.6:35b", CancellationToken.None);

        Assert.Contains("qwen3.6:35b", installed);
        Assert.NotNull(residency);
        Assert.Equal(75, residency.VramPercentage);
        Assert.False(residency.PredominantlyVramResident);
        Assert.Equal(["/api/tags", "/api/ps", "/api/generate"], handler.Requests.Select(item => item.Path));
        Assert.DoesNotContain(handler.Requests, item => item.Path.Contains("pull", StringComparison.OrdinalIgnoreCase));
        using var unload = JsonDocument.Parse(handler.Requests[^1].Body!);
        Assert.Equal(0, unload.RootElement.GetProperty("keep_alive").GetInt32());
    }

    [Fact]
    public async Task Chat_SendsActualSchemaMetricsAndExactFiveThinkModes()
    {
        var handler = new RecordingHandler(_ => Json("""
            {
              "message":{"content":"{\"recommendation\":\"keep\",\"confidence\":\"high\",\"category\":\"professional\",\"reasonCodes\":[\"professional_content\"],\"rationale\":\"Useful professional evidence.\"}","thinking":"private trace"},
              "total_duration":5000000000,"load_duration":2000000000,"prompt_eval_count":640,"prompt_eval_duration":800000000,"eval_count":80,"eval_duration":1600000000
            }
            """));
        var client = CreateClient(handler);
        var prompt = new ClassifierPromptSnapshot(
            ClassifierPromptDefinition.Version,
            ClassifierPromptDefinition.SystemPrompt,
            ClassifierPromptDefinition.SystemPromptSha256,
            ClassifierPromptDefinition.OutputSchemaVersion,
            ClassifierPromptDefinition.OutputJsonSchema);
        var profiles = new[]
        {
            new ClassifierModelProfile("qwen36-35b-nothink", "qwen3.6:35b", "false", 0, 8192, false, "30m"),
            new ClassifierModelProfile("gemma4-31b-default", "gemma4:31b", null, 0, 8192, false, "30m"),
            new ClassifierModelProfile("deepseek-r1-32b-thinking", "deepseek-r1:32b", "true", 0, 8192, false, "30m"),
            new ClassifierModelProfile("ornith-15-35b-default", "ornith-1.5:35b", null, 0, 8192, false, "30m"),
            new ClassifierModelProfile("gpt-oss-low", "gpt-oss:latest", "low", 0, 8192, false, "30m")
        };

        foreach (var profile in profiles)
        {
            var response = await client.ChatAsync(profile, prompt, "{\"messageCount\":10}", CancellationToken.None);
            Assert.Equal(5_000_000_000, response.TotalDurationNanoseconds);
            Assert.Equal(640, response.PromptEvalCount);
            Assert.Equal("private trace", response.Thinking);
        }

        Assert.Equal(5, handler.Requests.Count);
        for (var index = 0; index < profiles.Length; index++)
        {
            using var document = JsonDocument.Parse(handler.Requests[index].Body!);
            var root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.GetProperty("format").ValueKind);
            Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetDouble());
            Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
            Assert.Equal("30m", root.GetProperty("keep_alive").GetString());
            Assert.False(root.GetProperty("stream").GetBoolean());
            if (profiles[index].Think is null)
            {
                Assert.False(root.TryGetProperty("think", out _));
            }
            else if (profiles[index].Think is "true" or "false")
            {
                Assert.Equal(profiles[index].Think == "true", root.GetProperty("think").GetBoolean());
            }
            else
            {
                Assert.Equal("low", root.GetProperty("think").GetString());
            }
        }
    }

    [Fact]
    public async Task ClusterClassifier_ValidatesOutputWithoutPersistingThinkingText()
    {
        var api = new FakeApiClient();
        var classifier = new OllamaClusterClassifier(api);
        var response = await classifier.ClassifyAsync(
            new ClassifierModelProfile("p", "m", null, 0, 8192, false, "30m"),
            new ClassifierPromptSnapshot("v", "prompt", "hash", "schema", "{}"),
            new ClusterClassifierInput("sender", "a@example.test", "A", "Sender", 1, DateTime.UnixEpoch, DateTime.UnixEpoch, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []),
            CancellationToken.None);

        Assert.True(response.PrimaryMetrics.ThinkingPresent);
        Assert.Equal(22, response.PrimaryMetrics.ThinkingCharacterCount);
        Assert.Equal(ClassifierRecommendation.Keep, response.Output!.Recommendation);
        Assert.DoesNotContain(api.Response.Thinking!, JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_DisablesHttpClientTimeoutAndStillHonorsCallerCancellation()
    {
        var handler = new LongRunningChatHandler();
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(10) };
        var client = new OllamaApiClient(
            httpClient,
            Options.Create(new OllamaOptions { BaseUrl = "http://127.0.0.1:11434", MaxRetryAttempts = 1 }),
            new NoDelay());
        var profile = new ClassifierModelProfile("profile", "model", null, 0, 8192, false, "30m");
        var prompt = new ClassifierPromptSnapshot(
            ClassifierPromptDefinition.Version,
            ClassifierPromptDefinition.SystemPrompt,
            ClassifierPromptDefinition.SystemPromptSha256,
            ClassifierPromptDefinition.OutputSchemaVersion,
            ClassifierPromptDefinition.OutputJsonSchema);

        var completed = await client.ChatAsync(profile, prompt, "{\"messageCount\":10}", CancellationToken.None);

        Assert.Equal(Timeout.InfiniteTimeSpan, httpClient.Timeout);
        Assert.NotNull(completed.Content);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ChatAsync(profile, prompt, "{\"messageCount\":10}", cancellation.Token));
    }

    [Fact]
    public async Task RepairRequest_UsesSameProfileAndContainsOnlyPrimaryResponse()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"message":{"content":"{\"recommendation\":\"needs_review\",\"confidence\":\"low\",\"category\":\"unknown\",\"reasonCodes\":[]}"}}
            """));
        var client = CreateClient(handler);
        var profile = new ClassifierModelProfile("profile", "same-model", null, 0, 8192, false, "30m");
        var primaryResponse = "{\"recommendation\":\"unclear\",\"explanation\":\"Candidate explanation.\"}";

        await client.RepairAsync(profile, V2Prompt(), primaryResponse, CancellationToken.None);

        Assert.Single(handler.Requests);
        using var request = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("same-model", request.RootElement.GetProperty("model").GetString());
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal(ClassifierPromptV2Definition.RepairSystemPrompt, messages[0].GetProperty("content").GetString());
        var userPayload = messages[1].GetProperty("content").GetString()!;
        using var repairInput = JsonDocument.Parse(userPayload);
        Assert.Equal(primaryResponse, repairInput.RootElement.GetProperty("candidatePrimaryResponse").GetString());
        Assert.DoesNotContain("messageCount", userPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("groundTruth", userPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uniqueItems", handler.Requests[0].Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("maxItems", handler.Requests[0].Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPrimaryRecommendation_IsSelfRepairedOnceWithSeparateMetricsAndOriginalExplanation()
    {
        var primaryContent = """
            {"recommendation":"retain_this","confidence":"medium","category":"professional","reasonCodes":["professional_content"],"explanation":"Professional evidence supports retention, though the label was non-canonical."}
            """;
        var api = new RepairingApiClient(
            Chat(primaryContent, total: 240_000_000_000, load: 238_000_000_000),
            Chat("""{"recommendation":"keep","confidence":"medium","category":"professional","reasonCodes":["professional_content"]}""", total: 2_000_000_000, load: 10_000_000));
        var classifier = new OllamaClusterClassifier(api);
        var profile = new ClassifierModelProfile("profile", "model", null, 0, 8192, false, "30m");

        var result = await classifier.ClassifyAsync(profile, V2Prompt(), Input(), CancellationToken.None);

        Assert.Equal(1, api.RepairCalls);
        Assert.Same(profile, api.PrimaryProfile);
        Assert.Same(profile, api.RepairProfile);
        Assert.Equal(primaryContent, api.RepairInput);
        Assert.Equal(ClassifierNormalizationMode.SelfRepaired, result.NormalizationMode);
        Assert.Equal(ClassifierRecommendation.Keep, result.Output!.Recommendation);
        Assert.Equal("Professional evidence supports retention, though the label was non-canonical.", result.PrimaryExplanation);
        Assert.Equal(240_000_000_000, result.PrimaryMetrics.TotalDurationNanoseconds);
        Assert.Equal(2_000_000_000, result.RepairMetrics!.TotalDurationNanoseconds);
        Assert.DoesNotContain("thinking trace that must not persist", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidJson_RepairsAtMostOnceAndFailedRepairBecomesNormalizationFailure()
    {
        var api = new RepairingApiClient(
            Chat("not valid json", total: 9_000_000_000, load: 8_000_000_000),
            Chat("still not valid json", total: 1_000_000_000, load: 1_000_000));
        var classifier = new OllamaClusterClassifier(api);

        var result = await classifier.ClassifyAsync(
            new ClassifierModelProfile("profile", "model", null, 0, 8192, false, "30m"),
            V2Prompt(),
            Input(),
            CancellationToken.None);

        Assert.Equal(1, api.RepairCalls);
        Assert.Null(result.Output);
        Assert.Equal(ClassifierNormalizationMode.Failed, result.NormalizationMode);
        Assert.Equal("normalization_failed", result.NormalizationFailureCode);
        Assert.Equal("repair_invalid_json", result.RepairFailureCode);
        Assert.Equal("not valid json", result.PrimaryResponse);
    }

    private static ClassifierPromptSnapshot V2Prompt() => new(
        ClassifierPromptV2Definition.Version,
        ClassifierPromptV2Definition.SystemPrompt,
        ClassifierPromptV2Definition.SystemPromptSha256,
        ClassifierPromptV2Definition.OutputSchemaVersion,
        ClassifierPromptV2Definition.OutputJsonSchema,
        ClassifierResponseProtocol.NormalizeRepairV2,
        ClassifierPromptV2Definition.RepairPromptVersion,
        ClassifierPromptV2Definition.RepairSystemPrompt,
        ClassifierPromptV2Definition.RepairSystemPromptSha256,
        ClassifierPromptV2Definition.RepairOutputSchemaVersion,
        ClassifierPromptV2Definition.RepairOutputJsonSchema);

    private static ClusterClassifierInput Input() => new(
        "sender", "cluster@example.test", "Cluster", "Sender", 10,
        DateTime.UnixEpoch, DateTime.UnixEpoch, 1, 0, 0, 0, 10, 100, 0, 0, 0, 0,
        ["Synthetic evidence subject"]);

    private static OllamaChatResponse Chat(string content, long total, long load) => new(
        content, "thinking trace that must not persist", total, load, 100, 200, 20, 300);

    private static OllamaApiClient CreateClient(RecordingHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new OllamaOptions { BaseUrl = "http://127.0.0.1:11434", MaxRetryAttempts = 1 }),
        new NoDelay());

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class NoDelay : IOllamaRetryDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<(string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            return responseFactory(request);
        }
    }

    private sealed class LongRunningChatHandler : HttpMessageHandler
    {
        private int callCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(75), cancellationToken);
                return Json("""
                    {"message":{"content":"{\"recommendation\":\"keep\",\"confidence\":\"high\",\"category\":\"professional\",\"reasonCodes\":[\"professional_content\"],\"rationale\":\"Useful professional evidence.\"}"}}
                    """);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation should stop the synthetic request.");
        }
    }

    private sealed class FakeApiClient : IOllamaApiClient
    {
        public OllamaChatResponse Response { get; } = new(
            """{"recommendation":"keep","confidence":"high","category":"professional","reasonCodes":["professional_content"],"rationale":"Useful professional evidence."}""",
            "private thinking text!",
            1, 1, 1, 1, 1, 1);

        public Task<IReadOnlySet<string>> GetInstalledModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<OllamaChatResponse> ChatAsync(ClassifierModelProfile profile, ClassifierPromptSnapshot prompt, string evidenceJson, CancellationToken cancellationToken) =>
            Task.FromResult(Response);

        public Task<OllamaChatResponse> RepairAsync(ClassifierModelProfile profile, ClassifierPromptSnapshot prompt, string primaryResponse, CancellationToken cancellationToken) =>
            Task.FromResult(Response);

        public Task<ModelResidency?> GetResidencyAsync(string model, CancellationToken cancellationToken) => Task.FromResult<ModelResidency?>(null);
        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RepairingApiClient(OllamaChatResponse primary, OllamaChatResponse repair) : IOllamaApiClient
    {
        public int RepairCalls { get; private set; }
        public ClassifierModelProfile? PrimaryProfile { get; private set; }
        public ClassifierModelProfile? RepairProfile { get; private set; }
        public string? RepairInput { get; private set; }

        public Task<OllamaChatResponse> ChatAsync(
            ClassifierModelProfile profile,
            ClassifierPromptSnapshot prompt,
            string evidenceJson,
            CancellationToken cancellationToken)
        {
            PrimaryProfile = profile;
            return Task.FromResult(primary);
        }

        public Task<OllamaChatResponse> RepairAsync(
            ClassifierModelProfile profile,
            ClassifierPromptSnapshot prompt,
            string primaryResponse,
            CancellationToken cancellationToken)
        {
            RepairCalls++;
            RepairProfile = profile;
            RepairInput = primaryResponse;
            return Task.FromResult(repair);
        }

        public Task<IReadOnlySet<string>> GetInstalledModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<ModelResidency?> GetResidencyAsync(string model, CancellationToken cancellationToken) =>
            Task.FromResult<ModelResidency?>(null);

        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
