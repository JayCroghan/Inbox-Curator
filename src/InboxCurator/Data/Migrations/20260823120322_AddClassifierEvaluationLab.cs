using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxCurator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClassifierEvaluationLab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassifierPromptVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SystemPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    SystemPromptSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OutputSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OutputJsonSchema = table.Column<string>(type: "TEXT", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassifierPromptVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationCorpora",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SplitStrategy = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    EligibleSourceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ExcludedCleanExistingOnlyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ExcludedCleanOlderThanCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ExcludedDeferCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ExcludedMissingEvidenceCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCorpora", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassifierRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EvaluationCorpusId = table.Column<long>(type: "INTEGER", nullable: false),
                    ClassifierPromptVersionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TotalItems = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedItems = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedItems = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentProfileKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CurrentSplit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelRequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassifierRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassifierRuns_ClassifierPromptVersions_ClassifierPromptVersionId",
                        column: x => x.ClassifierPromptVersionId,
                        principalTable: "ClassifierPromptVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassifierRuns_EvaluationCorpora_EvaluationCorpusId",
                        column: x => x.EvaluationCorpusId,
                        principalTable: "EvaluationCorpora",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationCorpusItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EvaluationCorpusId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TargetValue = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    GroundTruth = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Split = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstReceivedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastReceivedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UnreadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StarredCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RelationshipCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PromotionsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ListUnsubscribeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AttachmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RepresentativeSubjectsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCorpusItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationCorpusItems_EvaluationCorpora_EvaluationCorpusId",
                        column: x => x.EvaluationCorpusId,
                        principalTable: "EvaluationCorpora",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClassifierRunProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClassifierRunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProfileKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ThinkMode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Temperature = table.Column<double>(type: "REAL", nullable: false),
                    ContextLength = table.Column<int>(type: "INTEGER", nullable: false),
                    Stream = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeepAlive = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ExecutionOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ColdLoadDurationNanoseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ModelSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    SizeVramBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    RuntimeContextLength = table.Column<int>(type: "INTEGER", nullable: true),
                    PredominantlyVramResident = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassifierRunProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassifierRunProfiles_ClassifierRuns_ClassifierRunId",
                        column: x => x.ClassifierRunId,
                        principalTable: "ClassifierRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClassifierResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClassifierRunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ClassifierRunProfileId = table.Column<long>(type: "INTEGER", nullable: false),
                    EvaluationCorpusItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Recommendation = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ReasonCodesJson = table.Column<string>(type: "TEXT", nullable: true),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ThinkingPresent = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThinkingCharacterCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsColdLoadRequest = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalDurationNanoseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    LoadDurationNanoseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    PromptEvalCount = table.Column<int>(type: "INTEGER", nullable: true),
                    PromptEvalDurationNanoseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    EvalCount = table.Column<int>(type: "INTEGER", nullable: true),
                    EvalDurationNanoseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassifierResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassifierResults_ClassifierRunProfiles_ClassifierRunProfileId",
                        column: x => x.ClassifierRunProfileId,
                        principalTable: "ClassifierRunProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassifierResults_ClassifierRuns_ClassifierRunId",
                        column: x => x.ClassifierRunId,
                        principalTable: "ClassifierRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassifierResults_EvaluationCorpusItems_EvaluationCorpusItemId",
                        column: x => x.EvaluationCorpusItemId,
                        principalTable: "EvaluationCorpusItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierPromptVersions_Version",
                table: "ClassifierPromptVersions",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierResults_ClassifierRunId_Status",
                table: "ClassifierResults",
                columns: new[] { "ClassifierRunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierResults_ClassifierRunProfileId_EvaluationCorpusItemId",
                table: "ClassifierResults",
                columns: new[] { "ClassifierRunProfileId", "EvaluationCorpusItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierResults_EvaluationCorpusItemId",
                table: "ClassifierResults",
                column: "EvaluationCorpusItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierRunProfiles_ClassifierRunId_ProfileKey",
                table: "ClassifierRunProfiles",
                columns: new[] { "ClassifierRunId", "ProfileKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierRuns_ClassifierPromptVersionId",
                table: "ClassifierRuns",
                column: "ClassifierPromptVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassifierRuns_EvaluationCorpusId_ClassifierPromptVersionId_Stage",
                table: "ClassifierRuns",
                columns: new[] { "EvaluationCorpusId", "ClassifierPromptVersionId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCorpora_Version",
                table: "EvaluationCorpora",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCorpusItems_EvaluationCorpusId_Split",
                table: "EvaluationCorpusItems",
                columns: new[] { "EvaluationCorpusId", "Split" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCorpusItems_EvaluationCorpusId_TargetType_TargetValue",
                table: "EvaluationCorpusItems",
                columns: new[] { "EvaluationCorpusId", "TargetType", "TargetValue" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassifierResults");

            migrationBuilder.DropTable(
                name: "ClassifierRunProfiles");

            migrationBuilder.DropTable(
                name: "EvaluationCorpusItems");

            migrationBuilder.DropTable(
                name: "ClassifierRuns");

            migrationBuilder.DropTable(
                name: "ClassifierPromptVersions");

            migrationBuilder.DropTable(
                name: "EvaluationCorpora");
        }
    }
}
