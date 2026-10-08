using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<CustomValueDefinition> CustomValueDefinitions => Set<CustomValueDefinition>();
    public DbSet<CustomStoredValue> CustomStoredValues => Set<CustomStoredValue>();
    public DbSet<CustomCommandComputedResult> CustomCommandComputedResults =>
        Set<CustomCommandComputedResult>();

    private static void ConfigureStoredValues(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CustomValueDefinition>(b =>
        {
            _ = b.ToTable("custom_value_definitions");
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.NameHash).HasMaxLength(64);
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.Scope,
                    x.NameHash,
                })
                .IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CustomStoredValue>(b =>
        {
            _ = b.ToTable("custom_stored_values");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.TargetHash).HasMaxLength(64);
            _ = b.HasIndex(x => new { x.DefinitionId, x.TargetHash }).IsUnique();
            _ = b.HasOne<CustomValueDefinition>()
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.DefinitionId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<CustomCommandComputedResult>(b =>
        {
            _ = b.ToTable("custom_command_computed_results");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.InvocationHash).HasMaxLength(64);
            _ = b.HasIndex(x => new { x.HostId, x.InvocationHash }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
