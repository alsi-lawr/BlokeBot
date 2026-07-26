using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchPollStatusKinds =
        PersistedEnumTokens<TwitchPollStatus>.Values.ToArray();

    private static void ConfigurePolls(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HostBroadcasterAuthorization>(b =>
        {
            b.ToTable("host_broadcaster_authorizations");
            b.HasKey(x => x.Id);
            b.Property(x => x.TwitchUserId).HasMaxLength(64);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.AuthorizedScopes).HasMaxLength(512);
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwitchPollTemplate>(b =>
        {
            b.ToTable("twitch_poll_templates");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(60);
            b.HasIndex(x => x.HostId);
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Choices)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TwitchPollTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwitchPollTemplateChoice>(b =>
        {
            b.ToTable("twitch_poll_template_choices");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(25);
            b.HasIndex(x => new { x.TwitchPollTemplateId, x.Position }).IsUnique();
        });

        modelBuilder.Entity<TwitchPoll>(b =>
        {
            b.ToTable(
                "twitch_polls",
                t =>
                    t.HasCheckConstraint(
                        "CK_twitch_polls_Status",
                        KindIn("Status", _twitchPollStatusKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderPollId).HasMaxLength(128);
            b.Property(x => x.Title).HasMaxLength(60);
            b.Property(x => x.ChoicesJson).HasMaxLength(4096);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchPollStatus>.Format(status),
                    token => PersistedEnumTokens<TwitchPollStatus>.Parse(token)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.ProviderPollId }).IsUnique();
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" = 'Active'");
            b.HasIndex(x => new { x.HostId, x.EndedAtUtc });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
