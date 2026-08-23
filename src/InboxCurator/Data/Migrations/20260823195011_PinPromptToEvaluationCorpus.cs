using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxCurator.Data.Migrations
{
    /// <inheritdoc />
    public partial class PinPromptToEvaluationCorpus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite can add this nullable REFERENCES column in place. Expressing the
            // foreign key as a separate migration operation would rebuild the table
            // outside a transaction and emit a migration safety warning.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ClassifierPromptVersions"
                ADD "LockedEvaluationCorpusId" INTEGER NULL
                REFERENCES "EvaluationCorpora" ("Id") ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierPromptVersions_LockedEvaluationCorpusId",
                table: "ClassifierPromptVersions",
                column: "LockedEvaluationCorpusId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClassifierPromptVersions_LockedEvaluationCorpusId",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "LockedEvaluationCorpusId",
                table: "ClassifierPromptVersions");
        }
    }
}
