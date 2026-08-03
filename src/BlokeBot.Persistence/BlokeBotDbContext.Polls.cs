using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchPollStatusKinds =
        PersistedEnumTokens<TwitchPollStatus>.Values.ToArray();

    private static void ConfigurePolls(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<HostBroadcasterAuthorization>(static b =>
        {
            _ = b.ToTable("host_broadcaster_authorizations");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(64);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.AuthorizedScopes).HasMaxLength(512);
            _ = b.HasIndex(static x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<TwitchPollTemplate>(static b =>
        {
            _ = b.ToTable("twitch_poll_templates");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Title).HasMaxLength(60);
            _ = b.HasIndex(static x => x.HostId);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Choices)
                .WithOne(static x => x.Template)
                .HasForeignKey(static x => x.TwitchPollTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<TwitchPollTemplateChoice>(static b =>
        {
            _ = b.ToTable("twitch_poll_template_choices");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Title).HasMaxLength(25);
            _ = b.HasIndex(static x => new { x.TwitchPollTemplateId, x.Position }).IsUnique();
        });

        _ = modelBuilder.Entity<TwitchPoll>(static b =>
        {
            _ = b.ToTable(
                "twitch_polls",
                static t =>
                    t.HasCheckConstraint(
                        "CK_twitch_polls_Status",
                        KindIn("Status", _twitchPollStatusKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.ProviderPollId).HasMaxLength(128);
            _ = b.Property(static x => x.Title).HasMaxLength(60);
            _ = b.Property(static x => x.ChoicesJson).HasMaxLength(4096);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static status => PersistedEnumTokens<TwitchPollStatus>.Format(status),
                    static token => PersistedEnumTokens<TwitchPollStatus>.Parse(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.HostId, x.ProviderPollId }).IsUnique();
            _ = b.HasIndex(static x => x.HostId).IsUnique().HasFilter("\"Status\" = 'Active'");
            _ = b.HasIndex(static x => new { x.HostId, x.EndedAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
