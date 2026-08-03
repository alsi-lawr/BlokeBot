using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchPollStatusKinds =
        PersistedEnumTokens<TwitchPollStatus>.Values.ToArray();

    private static void ConfigurePolls(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<HostBroadcasterAuthorization>(b =>
        {
            _ = b.ToTable("host_broadcaster_authorizations");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.Login).HasMaxLength(128);
            _ = b.Property(x => x.AuthorizedScopes).HasMaxLength(512);
            _ = b.HasIndex(x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<TwitchPollTemplate>(b =>
        {
            _ = b.ToTable("twitch_poll_templates");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Title).HasMaxLength(60);
            _ = b.HasIndex(x => x.HostId);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Choices)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TwitchPollTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<TwitchPollTemplateChoice>(b =>
        {
            _ = b.ToTable("twitch_poll_template_choices");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Title).HasMaxLength(25);
            _ = b.HasIndex(x => new { x.TwitchPollTemplateId, x.Position }).IsUnique();
        });

        _ = modelBuilder.Entity<TwitchPoll>(b =>
        {
            _ = b.ToTable(
                "twitch_polls",
                t =>
                    t.HasCheckConstraint(
                        "CK_twitch_polls_Status",
                        KindIn("Status", _twitchPollStatusKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ProviderPollId).HasMaxLength(128);
            _ = b.Property(x => x.Title).HasMaxLength(60);
            _ = b.Property(x => x.ChoicesJson).HasMaxLength(4096);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchPollStatus>.Format(status),
                    token => PersistedEnumTokens<TwitchPollStatus>.Parse(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.ProviderPollId }).IsUnique();
            _ = b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" = 'Active'");
            _ = b.HasIndex(x => new { x.HostId, x.EndedAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
