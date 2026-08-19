using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxCurator.Data.Migrations;

[DbContext(typeof(InboxCuratorDbContext))]
[Migration("202608190002_HumanSeedDecisions")]
public sealed class HumanSeedDecisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClusterDecisions",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                TargetType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                TargetValue = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                GroupKey = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                DecisionKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                CutoffDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                AppliesToFuture = table.Column<bool>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                Revision = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ClusterDecisions", item => item.Id));

        migrationBuilder.CreateTable(
            name: "ClusterDecisionAudits",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                ClusterDecisionId = table.Column<long>(type: "INTEGER", nullable: false),
                ChangeKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                DecisionKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                CutoffDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                AppliesToFuture = table.Column<bool>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                Revision = table.Column<int>(type: "INTEGER", nullable: false),
                MatchingMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClusterDecisionAudits", item => item.Id);
                table.ForeignKey(
                    name: "FK_ClusterDecisionAudits_ClusterDecisions_ClusterDecisionId",
                    column: item => item.ClusterDecisionId,
                    principalTable: "ClusterDecisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ClusterDecisionAudits_ClusterDecisionId_Revision",
            table: "ClusterDecisionAudits",
            columns: ["ClusterDecisionId", "Revision"],
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_ClusterDecisions_GroupKey",
            table: "ClusterDecisions",
            column: "GroupKey");
        migrationBuilder.CreateIndex(
            name: "IX_ClusterDecisions_TargetType_TargetValue",
            table: "ClusterDecisions",
            columns: ["TargetType", "TargetValue"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ClusterDecisionAudits");
        migrationBuilder.DropTable(name: "ClusterDecisions");
    }
}
