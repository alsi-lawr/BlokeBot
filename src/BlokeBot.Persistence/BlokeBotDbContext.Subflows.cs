using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<AutomationSubflow> AutomationSubflows => Set<AutomationSubflow>();
    public DbSet<AutomationSubflowRevisionRecord> AutomationSubflowRevisions =>
        Set<AutomationSubflowRevisionRecord>();
    public DbSet<AutomationSubflowCallerReference> AutomationSubflowCallers =>
        Set<AutomationSubflowCallerReference>();
    public DbSet<AutomationSubflowRevisionReference> AutomationSubflowRevisionReferences =>
        Set<AutomationSubflowRevisionReference>();
    public DbSet<AutomationSubflowRunReference> AutomationSubflowRunReferences =>
        Set<AutomationSubflowRunReference>();

    private static void ConfigureSubflows(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<AutomationSubflow>(b =>
        {
            _ = b.ToTable("automation_subflows");
            _ = b.HasKey(x => new { x.HostId, x.Id });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<AutomationSubflowRevisionRecord>(b =>
        {
            _ = b.ToTable("automation_subflow_revisions");
            _ = b.HasKey(x => new { x.HostId, x.Id });
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.SubflowId,
                    x.Revision,
                })
                .IsUnique();
            _ = b.HasOne<AutomationSubflow>()
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.SubflowId })
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<AutomationSubflowCallerReference>(b =>
        {
            _ = b.ToTable("automation_subflow_callers");
            _ = b.HasKey(x => x.NodeId);
            _ = b.HasOne<AutomationFlowNode>()
                .WithMany()
                .HasForeignKey(x => x.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<AutomationSubflowRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.RevisionId })
                .OnDelete(DeleteBehavior.NoAction);
        });
        _ = modelBuilder.Entity<AutomationSubflowRevisionReference>(b =>
        {
            _ = b.ToTable("automation_subflow_revision_references");
            _ = b.HasKey(x => new
            {
                x.HostId,
                x.CallerRevisionId,
                x.NodeId,
            });
            _ = b.HasOne<AutomationSubflowRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.CallerRevisionId })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<AutomationSubflowRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.RevisionId })
                .OnDelete(DeleteBehavior.NoAction);
        });
        _ = modelBuilder.Entity<AutomationSubflowRunReference>(b =>
        {
            _ = b.ToTable("automation_subflow_run_references");
            _ = b.HasKey(x => new { x.RunId, x.RevisionId });
            _ = b.HasOne<AutomationFlowRun>()
                .WithMany()
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<AutomationSubflowRevisionRecord>()
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.RevisionId })
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
