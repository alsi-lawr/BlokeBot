using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<AutomationSourceAdmission> AutomationSourceAdmissions =>
        Set<AutomationSourceAdmission>();
    public DbSet<AutomationCountdown> AutomationCountdowns => Set<AutomationCountdown>();
    public DbSet<AutomationStreamObservation> AutomationStreamObservations =>
        Set<AutomationStreamObservation>();
    public DbSet<AutomationSeenViewer> AutomationSeenViewers => Set<AutomationSeenViewer>();
    public DbSet<AutomationGoalObservation> AutomationGoalObservations =>
        Set<AutomationGoalObservation>();
    public DbSet<AutomationGoalMilestone> AutomationGoalMilestones =>
        Set<AutomationGoalMilestone>();

    private static void ConfigureAutomationSources(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<AutomationSourceAdmission>(b =>
        {
            _ = b.ToTable("automation_source_admissions");
            _ = b.HasKey(x => x.HostId);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<AutomationCountdown>(b =>
        {
            _ = b.ToTable("automation_countdowns");
            _ = b.HasKey(x => new { x.HostId, x.Name });
            _ = b.Property(x => x.Name).HasMaxLength(96);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<AutomationStreamObservation>(b =>
        {
            _ = b.ToTable("automation_stream_observations");
            _ = b.HasKey(x => x.HostId);
            _ = b.Property(x => x.StreamId).HasMaxLength(128);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<AutomationSeenViewer>(b =>
        {
            _ = b.ToTable("automation_seen_viewers");
            _ = b.HasKey(x => new { x.HostId, x.ViewerId });
            _ = b.Property(x => x.ViewerId).HasMaxLength(128);
            _ = b.HasOne<AutomationStreamObservation>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<AutomationGoalObservation>(b =>
        {
            _ = b.ToTable("automation_goal_observations");
            _ = b.HasKey(x => new { x.HostId, x.GoalId });
            _ = b.Property(x => x.GoalId).HasMaxLength(128);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<AutomationGoalMilestone>(b =>
        {
            _ = b.ToTable("automation_goal_milestones");
            _ = b.HasKey(x => new
            {
                x.HostId,
                x.GoalId,
                x.Amount,
            });
            _ = b.Property(x => x.GoalId).HasMaxLength(128);
            _ = b.HasOne<AutomationGoalObservation>()
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.GoalId })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
