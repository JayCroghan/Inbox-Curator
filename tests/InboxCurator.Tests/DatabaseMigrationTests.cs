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
                ["202608190001_InitialCreate", "202608190002_HumanSeedDecisions", "20260823120322_AddClassifierEvaluationLab"],
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
