using InboxCurator.Classification;
using InboxCurator.Data;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Tests;

public sealed class ClassifierPromptVersionTests
{
    [Fact]
    public async Task EnsureAll_PreservesImmutableV1AndPersistsReproducibleV2RepairContract()
    {
        await using var store = new SqliteTestStore();
        await store.InitializeAsync();
        var service = new ClassifierPromptService(store.Factory, TimeProvider.System);

        await service.EnsureAllAsync();
        await service.EnsureAllAsync();

        await using var db = await store.Factory.CreateDbContextAsync();
        var prompts = await db.ClassifierPromptVersions.OrderBy(item => item.Version).ToArrayAsync();
        Assert.Equal(2, prompts.Length);
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
    }
}
