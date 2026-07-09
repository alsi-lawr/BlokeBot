using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDbContext(DbContextOptions<BlokeBotDbContext> options)
    : DbContext(options)
{
    public DbSet<BotHost> Hosts => Set<BotHost>();
    public DbSet<BotReplySettings> ReplySettings => Set<BotReplySettings>();
    public DbSet<CommandAlias> CommandAliases => Set<CommandAlias>();
    public DbSet<PointBalance> PointBalances => Set<PointBalance>();
    public DbSet<PointLedgerEntry> PointLedgerEntries => Set<PointLedgerEntry>();
    public DbSet<PointsGiveaway> PointsGiveaways => Set<PointsGiveaway>();
    public DbSet<PointsGiveawayEntrant> PointsGiveawayEntrants => Set<PointsGiveawayEntrant>();
    public DbSet<PointsGiveawayWinner> PointsGiveawayWinners => Set<PointsGiveawayWinner>();
    public DbSet<PointsSettings> PointsSettings => Set<PointsSettings>();
    public DbSet<GuessOption> GuessOptions => Set<GuessOption>();
    public DbSet<GuessRoundProfile> Profiles => Set<GuessRoundProfile>();
    public DbSet<GuessRound> Rounds => Set<GuessRound>();
    public DbSet<HostModAccessEntry> HostModAccessEntries => Set<HostModAccessEntry>();
    public DbSet<HostModAccessSettings> HostModAccessSettings => Set<HostModAccessSettings>();
    public DbSet<SiteAccessEntry> SiteAccessEntries => Set<SiteAccessEntry>();
    public DbSet<SiteAccessSettings> SiteAccessSettings => Set<SiteAccessSettings>();
    public DbSet<GuessVote> Votes => Set<GuessVote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BotHost>(b =>
        {
            b.ToTable("hosts");
            b.HasKey(x => x.Id);
            b.Property(x => x.BotRuntimeState);
            b.Property(x => x.BotRuntimeStateChangedAtUtc);
            b.Property(x => x.ChannelBotAuthorizedAtUtc);
            b.Property(x => x.ChannelBotAuthorizedScopes).HasMaxLength(512);
            b.Property(x => x.EnabledFeatures)
                .HasConversion(features => (long)features, value => (HostFeatureFlags)(ulong)value)
                .HasDefaultValue(HostFeatureFlags.All);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.Property(x => x.ProfileImageUrl).HasMaxLength(512);
            b.Property(x => x.TwitchUserId).HasMaxLength(64);
            b.HasIndex(x => x.Login).IsUnique();
        });

        modelBuilder.Entity<SiteAccessSettings>(b =>
        {
            b.ToTable("site_access_settings");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<SiteAccessEntry>(b =>
        {
            b.ToTable(
                "site_access_entries",
                t =>
                    t.HasCheckConstraint("CK_site_access_entries_Kind", KindIn("Kind", AccessKinds))
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => AccessListEntryKindStore.Format(kind),
                    value => AccessListEntryKindStore.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.Kind, x.Login }).IsUnique();
        });

        modelBuilder.Entity<HostModAccessSettings>(b =>
        {
            b.ToTable("host_mod_access_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.AllowModsByDefault).HasDefaultValue(true);
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HostModAccessEntry>(b =>
        {
            b.ToTable(
                "host_mod_access_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_host_mod_access_entries_Kind",
                        KindIn("Kind", AccessKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => AccessListEntryKindStore.Format(kind),
                    value => AccessListEntryKindStore.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Kind,
                    x.Login,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BotReplySettings>(b =>
        {
            b.ToTable("reply_settings");
            b.HasKey(x => x.Id);
            b.HasOne(x => x.GuessRoundProfile)
                .WithOne(x => x.ReplySettings)
                .HasForeignKey<BotReplySettings>(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommandAlias>(b =>
        {
            b.ToTable(
                "command_aliases",
                t =>
                    t.HasCheckConstraint(
                        "CK_command_aliases_Kind",
                        KindIn("Kind", CommandAliasKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => AppCommandKindStore.Format(kind),
                    value => AppCommandKindStore.Parse(value)
                )
                .HasMaxLength(64);
            b.Property(x => x.Alias).HasMaxLength(64);
            b.HasIndex(x => new { x.HostId, x.Alias }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointsSettings>(b =>
        {
            b.ToTable(
                "points_settings",
                t =>
                    t.HasCheckConstraint(
                        "CK_points_settings_GiveawayEligibility",
                        KindIn("GiveawayEligibility", PointsEligibilityKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.PointLabel).HasMaxLength(64);
            b.Property(x => x.GiveawayMinimumPayout).HasMaxLength(128);
            b.Property(x => x.GiveawayMaximumPayout).HasMaxLength(128);
            b.Property(x => x.GiveawayEligibility)
                .HasConversion(
                    mode => PointsEligibilityModeStore.Format(mode),
                    value => PointsEligibilityModeStore.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.FollowerEligibilityUnavailableReply)
                .HasColumnName("FollowerChecksUnavailableReply");
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointBalance>(b =>
        {
            b.ToTable("point_balances");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Amount).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.Login }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointLedgerEntry>(b =>
        {
            b.ToTable("point_ledger_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(64);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Delta).HasMaxLength(128);
            b.Property(x => x.BalanceAfter).HasMaxLength(128);
            b.Property(x => x.ActorLogin).HasMaxLength(128);
            b.Property(x => x.CounterpartyLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.CreatedAtUtc });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointsGiveaway>(b =>
        {
            b.ToTable(
                "points_giveaways",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_points_giveaways_Status",
                        KindIn("Status", PointsGiveawayStatusKinds)
                    );
                    t.HasCheckConstraint(
                        "CK_points_giveaways_Eligibility",
                        KindIn("Eligibility", PointsEligibilityKinds)
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.MinimumPayout).HasMaxLength(128);
            b.Property(x => x.MaximumPayout).HasMaxLength(128);
            b.Property(x => x.Eligibility)
                .HasConversion(
                    mode => PointsEligibilityModeStore.Format(mode),
                    value => PointsEligibilityModeStore.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" = 'Active'");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Entrants)
                .WithOne(x => x.Giveaway)
                .HasForeignKey(x => x.GiveawayId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Winners)
                .WithOne(x => x.Giveaway)
                .HasForeignKey(x => x.GiveawayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointsGiveawayEntrant>(b =>
        {
            b.ToTable("points_giveaway_entrants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.HasIndex(x => new { x.GiveawayId, x.Login }).IsUnique();
        });

        modelBuilder.Entity<PointsGiveawayWinner>(b =>
        {
            b.ToTable("points_giveaway_winners");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Payout).HasMaxLength(128);
        });

        modelBuilder.Entity<GuessOption>(b =>
        {
            b.ToTable("guess_options");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(128);
            b.HasIndex(x => new { x.GuessRoundProfileId, x.Name }).IsUnique();
            b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.Options)
                .HasForeignKey(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuessRoundProfile>(b =>
        {
            b.ToTable("guess_round_profiles");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.Slug).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.Slug }).IsUnique();
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"IsDefault\" = 1");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuessRound>(b =>
        {
            b.ToTable(
                "guess_rounds",
                t =>
                    t.HasCheckConstraint(
                        "CK_guess_rounds_Status",
                        KindIn("Status", GuessRoundStatusKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.WinningName).HasMaxLength(128);
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" IN ('Open', 'Closed')");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.Rounds)
                .HasForeignKey(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Votes)
                .WithOne(x => x.GuessRound)
                .HasForeignKey(x => x.GuessRoundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuessVote>(b =>
        {
            b.ToTable("guess_votes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.GuessName).HasMaxLength(128);
            b.HasIndex(x => new { x.GuessRoundId, x.Login }).IsUnique();
        });
    }

    private static readonly string[] AccessKinds = AccessListEntryKindStore.Values.ToArray();

    private static readonly string[] CommandAliasKinds = AppCommandKindStore.Values.ToArray();

    private static readonly string[] GuessRoundStatusKinds = ["Closed", "Completed", "Open"];

    private static readonly string[] PointsEligibilityKinds =
    [
        "everyone",
        "followers",
        "subscribers",
    ];

    private static readonly string[] PointsGiveawayStatusKinds =
    [
        "Active",
        "Cancelled",
        "Completed",
        "Expired",
    ];

    private static string KindIn(string columnName, IEnumerable<string> values) =>
        $"{columnName} IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
}
