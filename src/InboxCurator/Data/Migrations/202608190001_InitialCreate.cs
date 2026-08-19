using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxCurator.Data.Migrations;

[DbContext(typeof(InboxCuratorDbContext))]
[Migration("202608190001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Messages",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                GmailMessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ThreadId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                LabelIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                SenderName = table.Column<string>(type: "TEXT", nullable: true),
                SenderAddress = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                NormalizedSenderAddress = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                ReplyToAddress = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                Subject = table.Column<string>(type: "TEXT", maxLength: 998, nullable: false),
                ListId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                HasListUnsubscribe = table.Column<bool>(type: "INTEGER", nullable: false),
                HasAttachment = table.Column<bool>(type: "INTEGER", nullable: false),
                IsUnread = table.Column<bool>(type: "INTEGER", nullable: false),
                IsStarred = table.Column<bool>(type: "INTEGER", nullable: false),
                IsImportant = table.Column<bool>(type: "INTEGER", nullable: false),
                IsPromotion = table.Column<bool>(type: "INTEGER", nullable: false),
                HasDirectCorrespondence = table.Column<bool>(type: "INTEGER", nullable: false),
                HasThreadInteraction = table.Column<bool>(type: "INTEGER", nullable: false),
                GroupKey = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                GroupDisplay = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                GroupKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                LastSeenScanId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                IndexedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Messages", item => item.Id));

        migrationBuilder.CreateTable(
            name: "ScanCheckpoints",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                NextPageToken = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                ProcessedCount = table.Column<long>(type: "INTEGER", nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                FailureCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                ActiveRunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ScanCheckpoints", item => item.Id));

        migrationBuilder.CreateTable(
            name: "SentInteractions",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                GmailMessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ThreadId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                RecipientAddress = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastSeenScanId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SentInteractions", item => item.Id));

        migrationBuilder.CreateIndex(name: "IX_Messages_DateUtc", table: "Messages", column: "DateUtc");
        migrationBuilder.CreateIndex(name: "IX_Messages_GmailMessageId", table: "Messages", column: "GmailMessageId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Messages_GroupKey", table: "Messages", column: "GroupKey");
        migrationBuilder.CreateIndex(name: "IX_Messages_ThreadId", table: "Messages", column: "ThreadId");
        migrationBuilder.CreateIndex(name: "IX_ScanCheckpoints_Kind", table: "ScanCheckpoints", column: "Kind", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SentInteractions_GmailMessageId_RecipientAddress", table: "SentInteractions", columns: ["GmailMessageId", "RecipientAddress"], unique: true);
        migrationBuilder.CreateIndex(name: "IX_SentInteractions_RecipientAddress", table: "SentInteractions", column: "RecipientAddress");
        migrationBuilder.CreateIndex(name: "IX_SentInteractions_ThreadId", table: "SentInteractions", column: "ThreadId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Messages");
        migrationBuilder.DropTable(name: "ScanCheckpoints");
        migrationBuilder.DropTable(name: "SentInteractions");
    }
}
