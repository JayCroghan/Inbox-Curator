using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Data;

public sealed class InboxCuratorDbContext(DbContextOptions<InboxCuratorDbContext> options) : DbContext(options)
{
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();
    public DbSet<SentInteraction> SentInteractions => Set<SentInteraction>();
    public DbSet<ScanCheckpoint> ScanCheckpoints => Set<ScanCheckpoint>();
    public DbSet<ClusterDecision> ClusterDecisions => Set<ClusterDecision>();
    public DbSet<ClusterDecisionAudit> ClusterDecisionAudits => Set<ClusterDecisionAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var messages = modelBuilder.Entity<MessageRecord>();
        messages.ToTable("Messages");
        messages.HasKey(message => message.Id);
        messages.HasIndex(message => message.GmailMessageId).IsUnique();
        messages.HasIndex(message => message.ThreadId);
        messages.HasIndex(message => message.GroupKey);
        messages.HasIndex(message => message.DateUtc);
        messages.Property(message => message.GmailMessageId).HasMaxLength(128);
        messages.Property(message => message.ThreadId).HasMaxLength(128);
        messages.Property(message => message.SenderAddress).HasMaxLength(512);
        messages.Property(message => message.NormalizedSenderAddress).HasMaxLength(512);
        messages.Property(message => message.ReplyToAddress).HasMaxLength(512);
        messages.Property(message => message.Subject).HasMaxLength(998);
        messages.Property(message => message.ListId).HasMaxLength(512);
        messages.Property(message => message.GroupKey).HasMaxLength(600);
        messages.Property(message => message.GroupDisplay).HasMaxLength(512);
        messages.Property(message => message.GroupKind).HasMaxLength(16);
        messages.Property(message => message.LastSeenScanId).HasMaxLength(32);

        var interactions = modelBuilder.Entity<SentInteraction>();
        interactions.ToTable("SentInteractions");
        interactions.HasKey(interaction => interaction.Id);
        interactions.HasIndex(interaction => new { interaction.GmailMessageId, interaction.RecipientAddress }).IsUnique();
        interactions.HasIndex(interaction => interaction.ThreadId);
        interactions.HasIndex(interaction => interaction.RecipientAddress);
        interactions.Property(interaction => interaction.GmailMessageId).HasMaxLength(128);
        interactions.Property(interaction => interaction.ThreadId).HasMaxLength(128);
        interactions.Property(interaction => interaction.RecipientAddress).HasMaxLength(512);
        interactions.Property(interaction => interaction.LastSeenScanId).HasMaxLength(32);

        var checkpoints = modelBuilder.Entity<ScanCheckpoint>();
        checkpoints.ToTable("ScanCheckpoints");
        checkpoints.HasKey(checkpoint => checkpoint.Id);
        checkpoints.HasIndex(checkpoint => checkpoint.Kind).IsUnique();
        checkpoints.Property(checkpoint => checkpoint.Kind).HasConversion<string>().HasMaxLength(16);
        checkpoints.Property(checkpoint => checkpoint.State).HasConversion<string>().HasMaxLength(16);
        checkpoints.Property(checkpoint => checkpoint.NextPageToken).HasMaxLength(2048);
        checkpoints.Property(checkpoint => checkpoint.FailureCode).HasMaxLength(128);
        checkpoints.Property(checkpoint => checkpoint.ActiveRunId).HasMaxLength(32);

        var decisions = modelBuilder.Entity<ClusterDecision>();
        decisions.ToTable("ClusterDecisions");
        decisions.HasKey(decision => decision.Id);
        decisions.HasIndex(decision => new { decision.TargetType, decision.TargetValue }).IsUnique();
        decisions.HasIndex(decision => decision.GroupKey);
        decisions.Property(decision => decision.TargetType).HasConversion<string>().HasMaxLength(16);
        decisions.Property(decision => decision.TargetValue).HasMaxLength(512);
        decisions.Property(decision => decision.GroupKey).HasMaxLength(600);
        decisions.Property(decision => decision.DecisionKind).HasConversion<string>().HasMaxLength(40);

        var decisionAudits = modelBuilder.Entity<ClusterDecisionAudit>();
        decisionAudits.ToTable("ClusterDecisionAudits");
        decisionAudits.HasKey(audit => audit.Id);
        decisionAudits.HasIndex(audit => new { audit.ClusterDecisionId, audit.Revision }).IsUnique();
        decisionAudits.Property(audit => audit.ChangeKind).HasConversion<string>().HasMaxLength(16);
        decisionAudits.Property(audit => audit.DecisionKind).HasConversion<string>().HasMaxLength(40);
        decisionAudits.HasOne(audit => audit.ClusterDecision)
            .WithMany(decision => decision.AuditEntries)
            .HasForeignKey(audit => audit.ClusterDecisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
