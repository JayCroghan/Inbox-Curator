using InboxCurator.Classification;
using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Tests;

public sealed class ClassifierPromptVersionTests
{
    [Fact]
    public async Task EnsureAll_PreservesV1AndV2AndPersistsV3WithTheV2ResponseContracts()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var service = new ClassifierPromptService(store.Factory, TimeProvider.System);

        await service.EnsureAllAsync();
        await service.EnsureAllAsync();

        await using var db = await store.Factory.CreateDbContextAsync();
        var prompts = await db.ClassifierPromptVersions.OrderBy(item => item.Version).ToArrayAsync();
        Assert.Equal(3, prompts.Length);
        var v1 = prompts.Single(item => item.Version == ClassifierPromptDefinition.Version);
        Assert.Equal(ClassifierPromptDefinition.SystemPrompt, v1.SystemPrompt);
        Assert.Equal(ClassifierPromptDefinition.SystemPromptSha256, v1.SystemPromptSha256);
        Assert.Equal(ClassifierPromptDefinition.OutputJsonSchema, v1.OutputJsonSchema);
        Assert.Equal(ClassifierPromptDefinition.OutputJsonSchemaSha256, v1.OutputJsonSchemaSha256);
        Assert.Equal(ClassifierResponseProtocol.StrictV1, v1.ResponseProtocol);
        Assert.Null(v1.RepairPromptVersion);

        var v2 = prompts.Single(item => item.Version == ClassifierPromptV2Definition.Version);
        Assert.Equal(ClassifierResponseProtocol.NormalizeRepairV2, v2.ResponseProtocol);
        Assert.Equal(ClassifierPromptV2Definition.SystemPromptSha256, v2.SystemPromptSha256);
        Assert.Equal(ClassifierPromptV2Definition.OutputJsonSchemaSha256, v2.OutputJsonSchemaSha256);
        Assert.Equal(ClassifierPromptV2Definition.RepairPromptVersion, v2.RepairPromptVersion);
        Assert.Equal(ClassifierPromptV2Definition.RepairSystemPromptSha256, v2.RepairSystemPromptSha256);
        Assert.Equal(ClassifierPromptV2Definition.RepairOutputSchemaVersion, v2.RepairOutputSchemaVersion);
        Assert.Equal(ClassifierPromptV2Definition.RepairOutputJsonSchemaSha256, v2.RepairOutputJsonSchemaSha256);
        Assert.DoesNotContain("uniqueItems", v2.OutputJsonSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("maxItems", v2.OutputJsonSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("additionalProperties", v2.OutputJsonSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("uniqueItems", v2.RepairOutputJsonSchema!, StringComparison.Ordinal);
        Assert.DoesNotContain("maxItems", v2.RepairOutputJsonSchema!, StringComparison.Ordinal);
        Assert.DoesNotContain("enum", v2.RepairOutputJsonSchema!, StringComparison.Ordinal);
        Assert.DoesNotContain("confidence", v2.RepairOutputJsonSchema!, StringComparison.Ordinal);
        Assert.DoesNotContain("category", v2.RepairOutputJsonSchema!, StringComparison.Ordinal);
        Assert.DoesNotContain("reasonCodes", v2.RepairOutputJsonSchema!, StringComparison.Ordinal);
        Assert.Contains("Extract only", v2.RepairSystemPrompt!, StringComparison.Ordinal);

        var v3 = prompts.Single(item => item.Version == ClassifierPromptV3Definition.Version);
        Assert.Equal(ClassifierResponseProtocol.NormalizeRepairV2, v3.ResponseProtocol);
        Assert.Equal(ClassifierPromptV3Definition.SystemPrompt, v3.SystemPrompt);
        Assert.Equal(ClassifierPromptV3Definition.SystemPromptSha256, v3.SystemPromptSha256);
        Assert.NotEqual(v2.SystemPromptSha256, v3.SystemPromptSha256);
        Assert.Equal(v2.OutputSchemaVersion, v3.OutputSchemaVersion);
        Assert.Equal(v2.OutputJsonSchema, v3.OutputJsonSchema);
        Assert.Equal(v2.OutputJsonSchemaSha256, v3.OutputJsonSchemaSha256);
        Assert.Equal(v2.RepairPromptVersion, v3.RepairPromptVersion);
        Assert.Equal(v2.RepairSystemPrompt, v3.RepairSystemPrompt);
        Assert.Equal(v2.RepairSystemPromptSha256, v3.RepairSystemPromptSha256);
        Assert.Equal(v2.RepairOutputSchemaVersion, v3.RepairOutputSchemaVersion);
        Assert.Equal(v2.RepairOutputJsonSchema, v3.RepairOutputJsonSchema);
        Assert.Equal(v2.RepairOutputJsonSchemaSha256, v3.RepairOutputJsonSchemaSha256);
    }
}
