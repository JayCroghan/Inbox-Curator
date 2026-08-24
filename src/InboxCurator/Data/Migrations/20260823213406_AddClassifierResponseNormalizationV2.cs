using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxCurator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClassifierResponseNormalizationV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizationMode",
                table: "ClassifierResults",
                type: "TEXT",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizationWarningsJson",
                table: "ClassifierResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryExplanation",
                table: "ClassifierResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryResponse",
                table: "ClassifierResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawReasonCodesJson",
                table: "ClassifierResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepairEvalCount",
                table: "ClassifierResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RepairEvalDurationNanoseconds",
                table: "ClassifierResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairFailureCode",
                table: "ClassifierResults",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RepairLoadDurationNanoseconds",
                table: "ClassifierResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepairPromptEvalCount",
                table: "ClassifierResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RepairPromptEvalDurationNanoseconds",
                table: "ClassifierResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairResponse",
                table: "ClassifierResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RepairTotalDurationNanoseconds",
                table: "ClassifierResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SemanticWarningsJson",
                table: "ClassifierResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputJsonSchemaSha256",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairOutputJsonSchema",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairOutputJsonSchemaSha256",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairOutputSchemaVersion",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairPromptVersion",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairSystemPrompt",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairSystemPromptSha256",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseProtocol",
                table: "ClassifierPromptVersions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "StrictV1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizationMode",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "NormalizationWarningsJson",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "PrimaryExplanation",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "PrimaryResponse",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RawReasonCodesJson",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairEvalCount",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairEvalDurationNanoseconds",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairFailureCode",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairLoadDurationNanoseconds",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairPromptEvalCount",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairPromptEvalDurationNanoseconds",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairResponse",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "RepairTotalDurationNanoseconds",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "SemanticWarningsJson",
                table: "ClassifierResults");

            migrationBuilder.DropColumn(
                name: "OutputJsonSchemaSha256",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "RepairOutputJsonSchema",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "RepairOutputJsonSchemaSha256",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "RepairOutputSchemaVersion",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "RepairPromptVersion",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "RepairSystemPrompt",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "RepairSystemPromptSha256",
                table: "ClassifierPromptVersions");

            migrationBuilder.DropColumn(
                name: "ResponseProtocol",
                table: "ClassifierPromptVersions");
        }
    }
}
