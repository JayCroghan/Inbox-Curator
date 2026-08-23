using Microsoft.EntityFrameworkCore;

namespace InboxCurator.Data;

public sealed class InboxCuratorDbContext(DbContextOptions<InboxCuratorDbContext> options) : DbContext(options)
{
    public DbSet<MessageRecord> Messages => Set<MessageRecord>();
    public DbSet<SentInteraction> SentInteractions => Set<SentInteraction>();
    public DbSet<ScanCheckpoint> ScanCheckpoints => Set<ScanCheckpoint>();
    public DbSet<ClusterDecision> ClusterDecisions => Set<ClusterDecision>();
    public DbSet<ClusterDecisionAudit> ClusterDecisionAudits => Set<ClusterDecisionAudit>();
    public DbSet<EvaluationCorpus> EvaluationCorpora => Set<EvaluationCorpus>();
    public DbSet<EvaluationCorpusItem> EvaluationCorpusItems => Set<EvaluationCorpusItem>();
    public DbSet<ClassifierPromptVersion> ClassifierPromptVersions => Set<ClassifierPromptVersion>();
    public DbSet<ClassifierRun> ClassifierRuns => Set<ClassifierRun>();
    public DbSet<ClassifierRunProfile> ClassifierRunProfiles => Set<ClassifierRunProfile>();
    public DbSet<ClassifierResult> ClassifierResults => Set<ClassifierResult>();

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

        var corpora = modelBuilder.Entity<EvaluationCorpus>();
        corpora.ToTable("EvaluationCorpora");
        corpora.HasKey(corpus => corpus.Id);
        corpora.HasIndex(corpus => corpus.Version).IsUnique();
        corpora.Property(corpus => corpus.Version).HasMaxLength(64);
        corpora.Property(corpus => corpus.SplitStrategy).HasMaxLength(160);

        var corpusItems = modelBuilder.Entity<EvaluationCorpusItem>();
        corpusItems.ToTable("EvaluationCorpusItems");
        corpusItems.HasKey(item => item.Id);
        corpusItems.HasIndex(item => new { item.EvaluationCorpusId, item.TargetType, item.TargetValue }).IsUnique();
        corpusItems.HasIndex(item => new { item.EvaluationCorpusId, item.Split });
        corpusItems.Property(item => item.TargetType).HasConversion<string>().HasMaxLength(16);
        corpusItems.Property(item => item.TargetValue).HasMaxLength(512);
        corpusItems.Property(item => item.GroupKey).HasMaxLength(600);
        corpusItems.Property(item => item.DisplayName).HasMaxLength(512);
        corpusItems.Property(item => item.GroundTruth).HasConversion<string>().HasMaxLength(16);
        corpusItems.Property(item => item.Split).HasConversion<string>().HasMaxLength(16);
        corpusItems.HasOne(item => item.EvaluationCorpus)
            .WithMany(corpus => corpus.Items)
            .HasForeignKey(item => item.EvaluationCorpusId)
            .OnDelete(DeleteBehavior.Cascade);

        var prompts = modelBuilder.Entity<ClassifierPromptVersion>();
        prompts.ToTable("ClassifierPromptVersions");
        prompts.HasKey(prompt => prompt.Id);
        prompts.HasIndex(prompt => prompt.Version).IsUnique();
        prompts.Property(prompt => prompt.Version).HasMaxLength(64);
        prompts.Property(prompt => prompt.SystemPromptSha256).HasMaxLength(64);
        prompts.Property(prompt => prompt.OutputSchemaVersion).HasMaxLength(64);
        prompts.HasIndex(prompt => prompt.LockedEvaluationCorpusId);
        prompts.HasOne(prompt => prompt.LockedEvaluationCorpus)
            .WithMany(corpus => corpus.LockedPromptVersions)
            .HasForeignKey(prompt => prompt.LockedEvaluationCorpusId)
            .OnDelete(DeleteBehavior.Restrict);

        var runs = modelBuilder.Entity<ClassifierRun>();
        runs.ToTable("ClassifierRuns");
        runs.HasKey(run => run.Id);
        runs.HasIndex(run => new { run.EvaluationCorpusId, run.ClassifierPromptVersionId, run.Stage });
        runs.Property(run => run.Id).HasMaxLength(32);
        runs.Property(run => run.Stage).HasConversion<string>().HasMaxLength(32);
        runs.Property(run => run.State).HasConversion<string>().HasMaxLength(24);
        runs.Property(run => run.CurrentProfileKey).HasMaxLength(64);
        runs.Property(run => run.CurrentSplit).HasConversion<string>().HasMaxLength(16);
        runs.Property(run => run.FailureCode).HasMaxLength(64);
        runs.HasOne(run => run.EvaluationCorpus)
            .WithMany(corpus => corpus.Runs)
            .HasForeignKey(run => run.EvaluationCorpusId)
            .OnDelete(DeleteBehavior.Restrict);
        runs.HasOne(run => run.ClassifierPromptVersion)
            .WithMany(prompt => prompt.Runs)
            .HasForeignKey(run => run.ClassifierPromptVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        var runProfiles = modelBuilder.Entity<ClassifierRunProfile>();
        runProfiles.ToTable("ClassifierRunProfiles");
        runProfiles.HasKey(profile => profile.Id);
        runProfiles.HasIndex(profile => new { profile.ClassifierRunId, profile.ProfileKey }).IsUnique();
        runProfiles.Property(profile => profile.ClassifierRunId).HasMaxLength(32);
        runProfiles.Property(profile => profile.ProfileKey).HasMaxLength(64);
        runProfiles.Property(profile => profile.ModelName).HasMaxLength(128);
        runProfiles.Property(profile => profile.ThinkMode).HasMaxLength(16);
        runProfiles.Property(profile => profile.KeepAlive).HasMaxLength(16);
        runProfiles.Property(profile => profile.State).HasConversion<string>().HasMaxLength(16);
        runProfiles.HasOne(profile => profile.ClassifierRun)
            .WithMany(run => run.Profiles)
            .HasForeignKey(profile => profile.ClassifierRunId)
            .OnDelete(DeleteBehavior.Cascade);

        var results = modelBuilder.Entity<ClassifierResult>();
        results.ToTable("ClassifierResults");
        results.HasKey(result => result.Id);
        results.HasIndex(result => new { result.ClassifierRunProfileId, result.EvaluationCorpusItemId }).IsUnique();
        results.HasIndex(result => new { result.ClassifierRunId, result.Status });
        results.Property(result => result.ClassifierRunId).HasMaxLength(32);
        results.Property(result => result.Status).HasConversion<string>().HasMaxLength(24);
        results.Property(result => result.Recommendation).HasConversion<string>().HasMaxLength(16);
        results.Property(result => result.Confidence).HasConversion<string>().HasMaxLength(16);
        results.Property(result => result.Category).HasConversion<string>().HasMaxLength(32);
        results.Property(result => result.Rationale).HasMaxLength(240);
        results.Property(result => result.FailureCode).HasMaxLength(64);
        results.HasOne(result => result.ClassifierRun)
            .WithMany(run => run.Results)
            .HasForeignKey(result => result.ClassifierRunId)
            .OnDelete(DeleteBehavior.Cascade);
        results.HasOne(result => result.ClassifierRunProfile)
            .WithMany(profile => profile.Results)
            .HasForeignKey(result => result.ClassifierRunProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        results.HasOne(result => result.EvaluationCorpusItem)
            .WithMany(item => item.ClassifierResults)
            .HasForeignKey(result => result.EvaluationCorpusItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
