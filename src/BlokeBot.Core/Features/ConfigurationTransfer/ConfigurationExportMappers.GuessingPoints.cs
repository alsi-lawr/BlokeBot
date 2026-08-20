using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationExportMappers
{
    private static readonly AppCommandKind[] _pointsCommands =
    [
        AppCommandKind.Points,
        AppCommandKind.GivePoints,
        AppCommandKind.AddPoints,
        AppCommandKind.RemovePoints,
        AppCommandKind.Gamble,
        AppCommandKind.Giveaway,
        AppCommandKind.Join,
        AppCommandKind.EndGiveaway,
        AppCommandKind.CancelGiveaway,
    ];

    internal static async Task<GuessingSectionV1> GuessingAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var profiles = await db
            .Profiles.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.ReplySettings)
            .Include(x => x.CommandAliases)
            .Include(x => x.Options)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Slug)
            .ToArrayAsync(cancellationToken);
        var ids = LocalIds("profile", profiles.Select(x => x.Id));
        return new(profiles.Select(x => Profile(x, ids[x.Id])).ToArray());
    }

    internal static async Task<PointsSectionV1> PointsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var settings =
            await db
                .PointsSettings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.HostId == hostId, cancellationToken)
            ?? new PointsSettings { HostId = hostId };
        var aliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && x.GuessRoundProfileId == null
                && _pointsCommands.Contains(x.Kind)
            )
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Alias)
            .ToArrayAsync(cancellationToken);
        return new(
            settings.PointLabel,
            _pointsCommands
                .Select(kind => new CommandAliasesV1(
                    kind,
                    aliases.Where(x => x.Kind == kind).Select(x => x.Alias).ToArray()
                ))
                .ToArray(),
            PointReplies(settings),
            settings.GamblingWinRatePercent,
            settings.GamblingCooldownSeconds,
            settings.GiveawayDurationSeconds,
            settings.GiveawayMinimumPayout,
            settings.GiveawayMaximumPayout,
            settings.GiveawayWinnerCount,
            settings.GiveawayEligibility,
            settings.GiveawayCooldownSeconds
        );
    }

    private static GuessingProfileV1 Profile(GuessRoundProfile value, string id) =>
        new(
            id,
            value.Name,
            value.Slug,
            value.IsDefault,
            value.WinningGuessPointReward,
            value
                .CommandAliases.GroupBy(x => x.Kind)
                .OrderBy(x => x.Key)
                .Select(x => new CommandAliasesV1(
                    x.Key,
                    x.OrderBy(y => y.Alias).Select(y => y.Alias).ToArray()
                ))
                .ToArray(),
            GuessReplies(value.ReplySettings ?? new BotReplySettings()),
            value
                .Options.OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new GuessOptionV1(x.Name, x.ReplyText, x.ReplyTarget))
                .ToArray()
        );

    private static GuessingRepliesV1 GuessReplies(BotReplySettings value) =>
        new(
            value.RoundStartedReply,
            value.RoundAlreadyOpenReply,
            value.NoOpenRoundReply,
            value.GuessingStoppedReply,
            value.GuessingAlreadyStoppedReply,
            value.GuessingClosedReply,
            value.InvalidGuessReply,
            value.GuessUsageReply,
            value.AvailableGuessesReply,
            value.WinUsageReply,
            value.ModeratorOnlyReply,
            value.WinnerReply,
            value.NoWinnersReply
        );

    private static PointsRepliesV1 PointReplies(PointsSettings value) =>
        new(
            value.BalanceReply,
            value.OtherBalanceReply,
            value.TransferReply,
            value.AddReply,
            value.RemoveReply,
            value.InvalidAmountReply,
            value.InsufficientBalanceReply,
            value.ModeratorOnlyReply,
            value.GamblingWinReply,
            value.GamblingLoseReply,
            value.GiveawayStartedReply,
            value.GiveawayUpdateReply,
            value.GiveawayJoinedReply,
            value.GiveawayAlreadyJoinedReply,
            value.GiveawayEndedReply,
            value.GiveawayNoEntrantsReply,
            value.GiveawayCancelledReply,
            value.GiveawayAlreadyActiveReply,
            value.GiveawayNotActiveReply,
            value.GiveawayCooldownReply,
            value.StreamOfflineReply,
            value.NotEligibleReply,
            value.FollowerEligibilityUnavailableReply
        );
}
