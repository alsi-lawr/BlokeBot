using BlokeBot.Features.Commands;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.Points.Replies;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Configuration;

public sealed class PointsConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CommandAliasRegistry aliasRegistry,
    PointsChangeNotifier changes
)
{
    public const int MinimumGiveawayCooldownSeconds = 300;

    public async Task<PointsConfiguration> LoadConfigurationAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings =
            await db.PointsSettings.AsNoTracking().SingleOrDefaultAsync(x => x.HostId == hostId, ct)
            ?? new PointsSettings { HostId = hostId };
        var aliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var replyDelivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyDeliveryFeature.Points,
            ReplyDeliverySettingWriter.HostScopeId,
            ct
        );
        var whisperResponsesEnabled = await WhisperResponsesEnabledAsync(db, hostId, ct);

        return new PointsConfiguration
        {
            PointLabel = settings.PointLabel,
            Aliases = new PointsCommandAliasEditor
            {
                PointsAliases = JoinAliases(aliases, PointsCommandKind.Points),
                GivePointsAliases = JoinAliases(aliases, PointsCommandKind.GivePoints),
                AddPointsAliases = JoinAliases(aliases, PointsCommandKind.AddPoints),
                RemovePointsAliases = JoinAliases(aliases, PointsCommandKind.RemovePoints),
                GambleAliases = JoinAliases(aliases, PointsCommandKind.Gamble),
                GiveawayAliases = JoinAliases(aliases, PointsCommandKind.Giveaway),
                JoinAliases = JoinAliases(aliases, PointsCommandKind.Join),
                EndGiveawayAliases = JoinAliases(aliases, PointsCommandKind.EndGiveaway),
                CancelGiveawayAliases = JoinAliases(aliases, PointsCommandKind.CancelGiveaway),
            },
            Replies = PointsDefaults.Replies(settings),
            ReplyDelivery = replyDelivery,
            WhisperResponsesEnabled = whisperResponsesEnabled,
            GamblingWinRatePercent = settings.GamblingWinRatePercent,
            GamblingCooldownSeconds = settings.GamblingCooldownSeconds,
            GiveawayDurationSeconds = settings.GiveawayDurationSeconds,
            GiveawayMinimumPayout = settings.GiveawayMinimumPayout,
            GiveawayMaximumPayout = settings.GiveawayMaximumPayout,
            GiveawayWinnerCount = settings.GiveawayWinnerCount,
            GiveawayEligibility = settings.GiveawayEligibility,
            GiveawayCooldownSeconds = settings.GiveawayCooldownSeconds,
        };
    }

    public async Task SaveConfigurationAsync(
        int hostId,
        PointsConfiguration config,
        CancellationToken ct
    )
    {
        Validate(config);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.PointsSettings.SingleOrDefaultAsync(x => x.HostId == hostId, ct);
        if (settings is null)
        {
            settings = new PointsSettings { HostId = hostId };
            db.PointsSettings.Add(settings);
        }

        Apply(settings, config);
        await ReplyDeliverySettingWriter.ReplaceAsync(
            db,
            hostId,
            ReplyDeliveryFeature.Points,
            ReplyDeliverySettingWriter.HostScopeId,
            config.ReplyDelivery.Only(PointsReplyKeys.WhisperableKeys),
            ct
        );
        await SaveAliasesAsync(db, hostId, config.Aliases, ct);
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    private static void Apply(PointsSettings settings, PointsConfiguration config)
    {
        settings.PointLabel = string.IsNullOrWhiteSpace(config.PointLabel)
            ? "points"
            : config.PointLabel.Trim();
        settings.GamblingWinRatePercent = Math.Clamp(config.GamblingWinRatePercent, 0, 100);
        settings.GamblingCooldownSeconds = Math.Max(0, config.GamblingCooldownSeconds);
        settings.GiveawayDurationSeconds = Math.Max(1, config.GiveawayDurationSeconds);
        settings.GiveawayMinimumPayout = PointAmount
            .ParseAbsolute(config.GiveawayMinimumPayout)
            .ToString();
        settings.GiveawayMaximumPayout = PointAmount
            .ParseAbsolute(config.GiveawayMaximumPayout)
            .ToString();
        settings.GiveawayWinnerCount = Math.Max(1, config.GiveawayWinnerCount);
        settings.GiveawayEligibility = config.GiveawayEligibility;
        settings.GiveawayCooldownSeconds = Math.Max(
            MinimumGiveawayCooldownSeconds,
            config.GiveawayCooldownSeconds
        );

        settings.BalanceReply = config.Replies.BalanceReply.Trim();
        settings.OtherBalanceReply = config.Replies.OtherBalanceReply.Trim();
        settings.TransferReply = config.Replies.TransferReply.Trim();
        settings.AddReply = config.Replies.AddReply.Trim();
        settings.RemoveReply = config.Replies.RemoveReply.Trim();
        settings.InvalidAmountReply = config.Replies.InvalidAmountReply.Trim();
        settings.InsufficientBalanceReply = config.Replies.InsufficientBalanceReply.Trim();
        settings.ModeratorOnlyReply = config.Replies.ModeratorOnlyReply.Trim();
        settings.GamblingWinReply = config.Replies.GamblingWinReply.Trim();
        settings.GamblingLoseReply = config.Replies.GamblingLoseReply.Trim();
        settings.GiveawayStartedReply = config.Replies.GiveawayStartedReply.Trim();
        settings.GiveawayUpdateReply = config.Replies.GiveawayUpdateReply.Trim();
        settings.GiveawayJoinedReply = config.Replies.GiveawayJoinedReply.Trim();
        settings.GiveawayAlreadyJoinedReply = config.Replies.GiveawayAlreadyJoinedReply.Trim();
        settings.GiveawayEndedReply = config.Replies.GiveawayEndedReply.Trim();
        settings.GiveawayNoEntrantsReply = config.Replies.GiveawayNoEntrantsReply.Trim();
        settings.GiveawayCancelledReply = config.Replies.GiveawayCancelledReply.Trim();
        settings.GiveawayAlreadyActiveReply = config.Replies.GiveawayAlreadyActiveReply.Trim();
        settings.GiveawayNotActiveReply = config.Replies.GiveawayNotActiveReply.Trim();
        settings.GiveawayCooldownReply = config.Replies.GiveawayCooldownReply.Trim();
        settings.StreamOfflineReply = config.Replies.StreamOfflineReply.Trim();
        settings.NotEligibleReply = config.Replies.NotEligibleReply.Trim();
        settings.FollowerEligibilityUnavailableReply =
            config.Replies.FollowerEligibilityUnavailableReply.Trim();
    }

    private static string JoinAliases(List<CommandAlias> aliases, PointsCommandKind kind) =>
        CommandAliasRegistry.JoinAliases(aliases, PointsAppCommandKindMap.ToAppKind(kind));

    private static async Task<bool> WhisperResponsesEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .HostBotAccountSettings.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .Select(x => x.OverrideEnabled && x.WhisperResponsesEnabled)
            .SingleOrDefaultAsync(ct);

    private async Task SaveAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        PointsCommandAliasEditor aliases,
        CancellationToken ct
    )
    {
        await aliasRegistry.ReplaceAliasesAsync(
            db,
            hostId,
            PointsAppCommandKindMap.AppKinds,
            [
                new CommandAliasDraft(AppCommandKind.Points, aliases.PointsAliases),
                new CommandAliasDraft(AppCommandKind.GivePoints, aliases.GivePointsAliases),
                new CommandAliasDraft(AppCommandKind.AddPoints, aliases.AddPointsAliases),
                new CommandAliasDraft(AppCommandKind.RemovePoints, aliases.RemovePointsAliases),
                new CommandAliasDraft(AppCommandKind.Gamble, aliases.GambleAliases),
                new CommandAliasDraft(AppCommandKind.Giveaway, aliases.GiveawayAliases),
                new CommandAliasDraft(AppCommandKind.Join, aliases.JoinAliases),
                new CommandAliasDraft(AppCommandKind.EndGiveaway, aliases.EndGiveawayAliases),
                new CommandAliasDraft(AppCommandKind.CancelGiveaway, aliases.CancelGiveawayAliases),
            ],
            ct
        );
    }

    private static void Validate(PointsConfiguration config)
    {
        var min = PointAmount.ParseAbsolute(config.GiveawayMinimumPayout);
        var max = PointAmount.ParseAbsolute(config.GiveawayMaximumPayout);
        if (min.Value > max.Value)
            throw new InvalidOperationException(
                "The smallest giveaway prize cannot be larger than the largest prize."
            );

        if (min.Value % 10 != 0 || max.Value % 10 != 0)
            throw new InvalidOperationException("Giveaway prizes must be multiples of 10.");

        if (config.GamblingWinRatePercent is < 0 or > 100)
            throw new InvalidOperationException(
                "The chance of winning must be between 0% and 100%."
            );

        if (config.GamblingCooldownSeconds < 0)
            config.GamblingCooldownSeconds = 0;

        if (config.GiveawayCooldownSeconds < MinimumGiveawayCooldownSeconds)
            config.GiveawayCooldownSeconds = MinimumGiveawayCooldownSeconds;
    }
}
