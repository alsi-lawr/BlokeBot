using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureAutomations(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<AutomationFlow>(static b =>
        {
            _ = b.ToTable("automation_flows");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Name).HasMaxLength(200);
            _ = b.HasIndex(static x => new { x.HostId, x.IsEnabled });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Nodes)
                .WithOne(static x => x.Flow)
                .HasForeignKey(static x => x.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Edges)
                .WithOne(static x => x.Flow)
                .HasForeignKey(static x => x.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<AutomationFlowNode>(static b =>
        {
            _ = b.ToTable("automation_flow_nodes");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.DefinitionId).HasMaxLength(96);
            _ = b.Property(static x => x.DisplayAlias).HasMaxLength(200);
            _ = b.HasIndex(static x => new { x.FlowId, x.DefinitionId });
        });

        _ = modelBuilder.Entity<AutomationFlowEdge>(static b =>
        {
            _ = b.ToTable("automation_flow_edges");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind).HasConversion<string>().HasMaxLength(16);
            _ = b.Property(static x => x.SourcePortId).HasMaxLength(96);
            _ = b.Property(static x => x.TargetPortId).HasMaxLength(96);
            _ = b.HasIndex(static x => new { x.FlowId, x.TargetNodeId });
        });

        _ = modelBuilder.Entity<AutomationFlowRun>(static b =>
        {
            _ = b.ToTable("automation_flow_runs");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.SourceDefinitionId).HasMaxLength(96);
            _ = b.Property(static x => x.RequiredFeatures)
                .HasConversion(
                    static features => (long)features,
                    static value => (HostFeatureFlags)(ulong)value
                );
            _ = b.Property(static x => x.Status).HasConversion<string>().HasMaxLength(32);
            _ = b.HasIndex(static x => new
                {
                    x.FlowId,
                    x.SourceNodeId,
                    x.SourceOccurrenceId,
                })
                .IsUnique();
            _ = b.HasIndex(static x => new { x.HostId, x.Status });
            _ = b.HasOne(static x => x.Flow)
                .WithMany()
                .HasForeignKey(static x => x.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.NodeRuns)
                .WithOne(static x => x.Run)
                .HasForeignKey(static x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<AutomationEventReceipt>(static b =>
        {
            _ = b.ToTable("automation_event_receipts");
            _ = b.HasKey(static x => new
            {
                x.HostId,
                x.SourceDefinitionId,
                x.ProviderMessageId,
            });
            _ = b.Property(static x => x.SourceDefinitionId).HasMaxLength(96);
            _ = b.Property(static x => x.ProviderMessageId).HasMaxLength(128);
            _ = b.HasIndex(static x => x.ExpiresAtUtc);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<AutomationNodeRun>(static b =>
        {
            _ = b.ToTable("automation_node_runs");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Status).HasConversion<string>().HasMaxLength(32);
            _ = b.Property(static x => x.OutcomeCode).HasMaxLength(64);
            _ = b.HasIndex(static x => new { x.RunId, x.NodeId }).IsUnique();
            _ = b.HasIndex(static x => new { x.Status, x.AvailableAtUtc });
        });
    }
}
