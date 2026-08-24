using InboxCurator.Classification;
using InboxCurator.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task MigrateAsync_AppliesClassifierEvaluationSchemaToFreshDatabase()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"inbox-curator-migration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<InboxCuratorDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using var db = new InboxCuratorDbContext(options);

            await db.Database.MigrateAsync();

            var migrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.Equal(
                [
                    "202608190001_InitialCreate",
                    "202608190002_HumanSeedDecisions",
                    "20260823120322_AddClassifierEvaluationLab",
                    "20260823195011_PinPromptToEvaluationCorpus",
                    "20260823213406_AddClassifierResponseNormalizationV2"
                ],
                migrations);
            db.ClusterDecisions.Add(new ClusterDecision
            {
                TargetType = ClusterTargetType.ListId,
                TargetValue = "migration.example",
                GroupKey = "list:migration.example",
                DecisionKind = ClusterDecisionKind.Defer,
                IsActive = true,
                Revision = 1,
                CreatedAtUtc = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
            Assert.Single(await db.ClusterDecisions.ToListAsync());
            Assert.True(await db.Database.CanConnectAsync());
            Assert.Empty(await db.EvaluationCorpora.ToListAsync());
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ClassifierResults') WHERE name = 'RepairResponse';";
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task V2Migration_PreservesHistoricalV1PromptAndRunAsStrictProtocol()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"inbox-curator-v1-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<InboxCuratorDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using var db = new InboxCuratorDbContext(options);
            await db.Database.MigrateAsync("20260823195011_PinPromptToEvaluationCorpus");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO EvaluationCorpora
                    (Id, Version, CreatedAtUtc, SplitStrategy, EligibleSourceCount,
                     ExcludedCleanExistingOnlyCount, ExcludedCleanOlderThanCount,
                     ExcludedDeferCount, ExcludedMissingEvidenceCount)
                VALUES
                    (9001, 'legacy-corpus', '2026-08-23T00:00:00Z', 'legacy', 1, 0, 0, 0, 0);

                INSERT INTO ClassifierPromptVersions
                    (Id, Version, SystemPrompt, SystemPromptSha256, OutputSchemaVersion,
                     OutputJsonSchema, IsLocked, CreatedAtUtc, LockedAtUtc, LockedEvaluationCorpusId)
                VALUES
                    (9002, 'MAIL-003A-PROMPT-V1', 'legacy prompt', 'legacy-hash',
                     'MAIL-003A-OUTPUT-V1', '{{}}', 0, '2026-08-23T00:00:00Z', NULL, NULL);

                INSERT INTO ClassifierRuns
                    (Id, EvaluationCorpusId, ClassifierPromptVersionId, Stage, State,
                     TotalItems, CompletedItems, FailedItems, CurrentProfileKey, CurrentSplit,
                     CreatedAtUtc, StartedAtUtc, UpdatedAtUtc, CompletedAtUtc,
                     CancelRequestedAtUtc, FailureCode)
                VALUES
                    ('legacy-v1-run', 9001, 9002, 'DevelopmentValidation', 'Completed',
                     1, 0, 1, NULL, NULL, '2026-08-23T00:00:00Z',
                     '2026-08-23T00:00:00Z', '2026-08-23T00:01:00Z',
                     '2026-08-23T00:01:00Z', NULL, NULL);
                """);

            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();

            var prompt = await db.ClassifierPromptVersions.SingleAsync(item => item.Id == 9002);
            var run = await db.ClassifierRuns.Include(item => item.ClassifierPromptVersion)
                .SingleAsync(item => item.Id == "legacy-v1-run");
            Assert.Equal(ClassifierResponseProtocol.StrictV1, prompt.ResponseProtocol);
            Assert.Null(prompt.RepairPromptVersion);
            Assert.Null(prompt.OutputJsonSchemaSha256);
            Assert.Equal(ClassifierPromptDefinition.Version, run.ClassifierPromptVersion.Version);
            Assert.Equal(ClassifierRunState.Completed, run.State);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
