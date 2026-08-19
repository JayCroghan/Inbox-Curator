using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxCurator.Data.Migrations;

[DbContext(typeof(InboxCuratorDbContext))]
public sealed class InboxCuratorDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.9");

        modelBuilder.Entity("InboxCurator.Data.ClusterDecision", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasAnnotation("Sqlite:Autoincrement", true);
            entity.Property<bool>("AppliesToFuture").HasColumnType("INTEGER");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
            entity.Property<DateTime?>("CutoffDateUtc").HasColumnType("TEXT");
            entity.Property<ClusterDecisionKind>("DecisionKind").HasConversion<string>().HasMaxLength(40).HasColumnType("TEXT");
            entity.Property<string>("GroupKey").IsRequired().HasMaxLength(600).HasColumnType("TEXT");
            entity.Property<bool>("IsActive").HasColumnType("INTEGER");
            entity.Property<int>("Revision").HasColumnType("INTEGER");
            entity.Property<ClusterTargetType>("TargetType").HasConversion<string>().HasMaxLength(16).HasColumnType("TEXT");
            entity.Property<string>("TargetValue").IsRequired().HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("GroupKey");
            entity.HasIndex("TargetType", "TargetValue").IsUnique();
            entity.ToTable("ClusterDecisions");
        });

        modelBuilder.Entity("InboxCurator.Data.ClusterDecisionAudit", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasAnnotation("Sqlite:Autoincrement", true);
            entity.Property<bool>("AppliesToFuture").HasColumnType("INTEGER");
            entity.Property<ClusterDecisionChangeKind>("ChangeKind").HasConversion<string>().HasMaxLength(16).HasColumnType("TEXT");
            entity.Property<DateTime>("ChangedAtUtc").HasColumnType("TEXT");
            entity.Property<long>("ClusterDecisionId").HasColumnType("INTEGER");
            entity.Property<DateTime?>("CutoffDateUtc").HasColumnType("TEXT");
            entity.Property<ClusterDecisionKind>("DecisionKind").HasConversion<string>().HasMaxLength(40).HasColumnType("TEXT");
            entity.Property<bool>("IsActive").HasColumnType("INTEGER");
            entity.Property<int>("MatchingMessageCount").HasColumnType("INTEGER");
            entity.Property<int>("Revision").HasColumnType("INTEGER");
            entity.HasKey("Id");
            entity.HasIndex("ClusterDecisionId", "Revision").IsUnique();
            entity.ToTable("ClusterDecisionAudits");
        });

        modelBuilder.Entity("InboxCurator.Data.MessageRecord", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasAnnotation("Sqlite:Autoincrement", true);
            entity.Property<DateTime>("DateUtc").HasColumnType("TEXT");
            entity.Property<string>("GmailMessageId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            entity.Property<string>("GroupDisplay").IsRequired().HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string>("GroupKey").IsRequired().HasMaxLength(600).HasColumnType("TEXT");
            entity.Property<string>("GroupKind").IsRequired().HasMaxLength(16).HasColumnType("TEXT");
            entity.Property<bool>("HasAttachment").HasColumnType("INTEGER");
            entity.Property<bool>("HasDirectCorrespondence").HasColumnType("INTEGER");
            entity.Property<bool>("HasListUnsubscribe").HasColumnType("INTEGER");
            entity.Property<bool>("HasThreadInteraction").HasColumnType("INTEGER");
            entity.Property<DateTime>("IndexedAtUtc").HasColumnType("TEXT");
            entity.Property<bool>("IsImportant").HasColumnType("INTEGER");
            entity.Property<bool>("IsPromotion").HasColumnType("INTEGER");
            entity.Property<bool>("IsStarred").HasColumnType("INTEGER");
            entity.Property<bool>("IsUnread").HasColumnType("INTEGER");
            entity.Property<string>("LabelIdsJson").IsRequired().HasColumnType("TEXT");
            entity.Property<string>("LastSeenScanId").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            entity.Property<string>("ListId").HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string>("NormalizedSenderAddress").IsRequired().HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string>("ReplyToAddress").HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string>("SenderAddress").IsRequired().HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string>("SenderName").HasColumnType("TEXT");
            entity.Property<string>("Subject").IsRequired().HasMaxLength(998).HasColumnType("TEXT");
            entity.Property<string>("ThreadId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("DateUtc");
            entity.HasIndex("GmailMessageId").IsUnique();
            entity.HasIndex("GroupKey");
            entity.HasIndex("ThreadId");
            entity.ToTable("Messages");
        });

        modelBuilder.Entity("InboxCurator.Data.ScanCheckpoint", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasAnnotation("Sqlite:Autoincrement", true);
            entity.Property<string>("ActiveRunId").HasMaxLength(32).HasColumnType("TEXT");
            entity.Property<DateTime?>("CompletedAtUtc").HasColumnType("TEXT");
            entity.Property<string>("FailureCode").HasMaxLength(128).HasColumnType("TEXT");
            entity.Property<ScanKind>("Kind").HasConversion<string>().HasMaxLength(16).HasColumnType("TEXT");
            entity.Property<string>("NextPageToken").HasMaxLength(2048).HasColumnType("TEXT");
            entity.Property<long>("ProcessedCount").HasColumnType("INTEGER");
            entity.Property<DateTime?>("StartedAtUtc").HasColumnType("TEXT");
            entity.Property<ScanState>("State").HasConversion<string>().HasMaxLength(16).HasColumnType("TEXT");
            entity.Property<DateTime?>("UpdatedAtUtc").HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("Kind").IsUnique();
            entity.ToTable("ScanCheckpoints");
        });

        modelBuilder.Entity("InboxCurator.Data.SentInteraction", entity =>
        {
            entity.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasAnnotation("Sqlite:Autoincrement", true);
            entity.Property<DateTime>("DateUtc").HasColumnType("TEXT");
            entity.Property<string>("GmailMessageId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            entity.Property<string>("LastSeenScanId").IsRequired().HasMaxLength(32).HasColumnType("TEXT");
            entity.Property<string>("RecipientAddress").IsRequired().HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string>("ThreadId").IsRequired().HasMaxLength(128).HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("GmailMessageId", "RecipientAddress").IsUnique();
            entity.HasIndex("RecipientAddress");
            entity.HasIndex("ThreadId");
            entity.ToTable("SentInteractions");
        });

        modelBuilder.Entity("InboxCurator.Data.ClusterDecisionAudit", entity =>
        {
            entity.HasOne("InboxCurator.Data.ClusterDecision", "ClusterDecision")
                .WithMany("AuditEntries")
                .HasForeignKey("ClusterDecisionId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            entity.Navigation("ClusterDecision");
        });

        modelBuilder.Entity("InboxCurator.Data.ClusterDecision", entity =>
        {
            entity.Navigation("AuditEntries");
        });
#pragma warning restore 612, 618
    }
}
