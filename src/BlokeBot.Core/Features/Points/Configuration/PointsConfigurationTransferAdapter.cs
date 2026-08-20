using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Persistence;

namespace BlokeBot.Core.Features.Points.Configuration;

public sealed class PointsConfigurationTransferAdapter
{
    internal static async Task<IReadOnlyList<ConfigurationValidationIssue>> StageAsync(
        BlokeBotDbContext db,
        int hostId,
        PointsSectionV1 section,
        CancellationToken cancellationToken
    )
    {
        var aliases = section.CommandAliases.ToDictionary(x => x.Command, x => x.Aliases);
        var draft = new PointsConfiguration
        {
            PointLabel = section.PointLabel,
            Aliases = new PointsCommandAliasEditor
            {
                PointsAliases = Join(aliases, Persistence.Models.AppCommandKind.Points),
                GivePointsAliases = Join(aliases, Persistence.Models.AppCommandKind.GivePoints),
                AddPointsAliases = Join(aliases, Persistence.Models.AppCommandKind.AddPoints),
                RemovePointsAliases = Join(aliases, Persistence.Models.AppCommandKind.RemovePoints),
                GambleAliases = Join(aliases, Persistence.Models.AppCommandKind.Gamble),
                GiveawayAliases = Join(aliases, Persistence.Models.AppCommandKind.Giveaway),
                JoinAliases = Join(aliases, Persistence.Models.AppCommandKind.Join),
                EndGiveawayAliases = Join(aliases, Persistence.Models.AppCommandKind.EndGiveaway),
                CancelGiveawayAliases = Join(
                    aliases,
                    Persistence.Models.AppCommandKind.CancelGiveaway
                ),
            },
            Replies = MapReplies(section.Replies),
            GamblingWinRatePercent = section.GamblingWinRatePercent,
            GamblingCooldownSeconds = section.GamblingCooldownSeconds,
            GiveawayDurationSeconds = section.GiveawayDurationSeconds,
            GiveawayMinimumPayout = section.GiveawayMinimumPayout,
            GiveawayMaximumPayout = section.GiveawayMaximumPayout,
            GiveawayWinnerCount = section.GiveawayWinnerCount,
            GiveawayEligibility = section.GiveawayEligibility,
            GiveawayCooldownSeconds = section.GiveawayCooldownSeconds,
        };
        return await PointsConfigurationValidator
            .Validate(draft)
            .Match(
                async command =>
                {
                    var failure = await PointsConfigurationService.StageAsync(
                        db,
                        hostId,
                        command,
                        cancellationToken
                    );
                    return failure is null
                        ? []
                        :
                        [
                            new ConfigurationValidationIssue(
                                "sections.points.commandAliases",
                                failure.Message
                            ),
                        ];
                },
                errors =>
                    Task.FromResult<IReadOnlyList<ConfigurationValidationIssue>>(
                        errors
                            .Select(error => new ConfigurationValidationIssue(
                                "sections.points",
                                error.Message
                            ))
                            .ToArray()
                    )
            );
    }

    private static string Join(
        IReadOnlyDictionary<Persistence.Models.AppCommandKind, IReadOnlyList<string>> aliases,
        Persistence.Models.AppCommandKind kind
    ) => string.Join(", ", aliases.GetValueOrDefault(kind) ?? []);

    private static PointsReplySettingsEditor MapReplies(PointsRepliesV1 value) =>
        new()
        {
            BalanceReply = value.Balance,
            OtherBalanceReply = value.OtherBalance,
            TransferReply = value.Transfer,
            AddReply = value.Add,
            RemoveReply = value.Remove,
            InvalidAmountReply = value.InvalidAmount,
            InsufficientBalanceReply = value.InsufficientBalance,
            ModeratorOnlyReply = value.ModeratorOnly,
            GamblingWinReply = value.GamblingWin,
            GamblingLoseReply = value.GamblingLose,
            GiveawayStartedReply = value.GiveawayStarted,
            GiveawayUpdateReply = value.GiveawayUpdate,
            GiveawayJoinedReply = value.GiveawayJoined,
            GiveawayAlreadyJoinedReply = value.GiveawayAlreadyJoined,
            GiveawayEndedReply = value.GiveawayEnded,
            GiveawayNoEntrantsReply = value.GiveawayNoEntrants,
            GiveawayCancelledReply = value.GiveawayCancelled,
            GiveawayAlreadyActiveReply = value.GiveawayAlreadyActive,
            GiveawayNotActiveReply = value.GiveawayNotActive,
            GiveawayCooldownReply = value.GiveawayCooldown,
            StreamOfflineReply = value.StreamOffline,
            NotEligibleReply = value.NotEligible,
            FollowerEligibilityUnavailableReply = value.FollowerEligibilityUnavailable,
        };
}
