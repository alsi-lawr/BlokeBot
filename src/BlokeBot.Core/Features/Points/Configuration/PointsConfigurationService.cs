using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.Configuration;

public sealed class PointsConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointsChangeNotifier changes
)
{
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
            ReplyFeature.Points,
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
            ReplyDelivery = ReplyDeliveryEditor.From(replyDelivery),
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

    public IO<PointsConfigurationSaved, PointsConfigurationSaveFailure> SaveConfiguration(
        int hostId,
        PointsConfigurationSaveCommand command
    ) =>
        IO<PointsConfigurationSaved, PointsConfigurationSaveFailure>.Create(ct =>
            ExecuteSaveAsync(hostId, command, ct)
        );

    private async ValueTask<
        Result<PointsConfigurationSaved, PointsConfigurationSaveFailure>
    > ExecuteSaveAsync(int hostId, PointsConfigurationSaveCommand command, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var aliasFailure = await ReplaceAliasesAsync(db, hostId, command.Aliases, ct);
        if (aliasFailure is not null)
        {
            return Result<PointsConfigurationSaved, PointsConfigurationSaveFailure>.Error(
                aliasFailure
            );
        }

        var settings = await db.PointsSettings.SingleOrDefaultAsync(x => x.HostId == hostId, ct);
        if (settings is null)
        {
            settings = new PointsSettings { HostId = hostId };
            db.PointsSettings.Add(settings);
        }

        Apply(settings, command);
        await ReplyDeliverySettingWriter.ReplaceAsync(
            db,
            hostId,
            ReplyFeature.Points,
            ReplyDeliverySettingWriter.HostScopeId,
            command.ReplyDelivery.Only(PointsReplyKeys.WhisperableKeys),
            ct
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return Result<PointsConfigurationSaved, PointsConfigurationSaveFailure>.Success(new());
    }

    private static void Apply(PointsSettings settings, PointsConfigurationSaveCommand command)
    {
        settings.PointLabel = command.PointLabel;
        settings.GamblingWinRatePercent = command.GamblingWinRatePercent;
        settings.GamblingCooldownSeconds = command.GamblingCooldownSeconds;
        settings.GiveawayDurationSeconds = command.GiveawayDurationSeconds;
        settings.GiveawayMinimumPayout = command.GiveawayMinimumPayout.ToString();
        settings.GiveawayMaximumPayout = command.GiveawayMaximumPayout.ToString();
        settings.GiveawayWinnerCount = command.GiveawayWinnerCount;
        settings.GiveawayEligibility = command.GiveawayEligibility;
        settings.GiveawayCooldownSeconds = command.GiveawayCooldownSeconds;

        settings.BalanceReply = command.Replies.BalanceReply;
        settings.OtherBalanceReply = command.Replies.OtherBalanceReply;
        settings.TransferReply = command.Replies.TransferReply;
        settings.AddReply = command.Replies.AddReply;
        settings.RemoveReply = command.Replies.RemoveReply;
        settings.InvalidAmountReply = command.Replies.InvalidAmountReply;
        settings.InsufficientBalanceReply = command.Replies.InsufficientBalanceReply;
        settings.ModeratorOnlyReply = command.Replies.ModeratorOnlyReply;
        settings.GamblingWinReply = command.Replies.GamblingWinReply;
        settings.GamblingLoseReply = command.Replies.GamblingLoseReply;
        settings.GiveawayStartedReply = command.Replies.GiveawayStartedReply;
        settings.GiveawayUpdateReply = command.Replies.GiveawayUpdateReply;
        settings.GiveawayJoinedReply = command.Replies.GiveawayJoinedReply;
        settings.GiveawayAlreadyJoinedReply = command.Replies.GiveawayAlreadyJoinedReply;
        settings.GiveawayEndedReply = command.Replies.GiveawayEndedReply;
        settings.GiveawayNoEntrantsReply = command.Replies.GiveawayNoEntrantsReply;
        settings.GiveawayCancelledReply = command.Replies.GiveawayCancelledReply;
        settings.GiveawayAlreadyActiveReply = command.Replies.GiveawayAlreadyActiveReply;
        settings.GiveawayNotActiveReply = command.Replies.GiveawayNotActiveReply;
        settings.GiveawayCooldownReply = command.Replies.GiveawayCooldownReply;
        settings.StreamOfflineReply = command.Replies.StreamOfflineReply;
        settings.NotEligibleReply = command.Replies.NotEligibleReply;
        settings.FollowerEligibilityUnavailableReply = command
            .Replies
            .FollowerEligibilityUnavailableReply;
    }

    private static string JoinAliases(List<CommandAlias> aliases, PointsCommandKind kind) =>
        CommandAliasRegistry.JoinAliases(
            aliases,
            PointsAppCommandKindMap.ToAppKind(kind),
            new CommandAliasScope.Global()
        );

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

    private static async Task<PointsConfigurationSaveFailure?> ReplaceAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        PointsCommandAliases aliases,
        CancellationToken ct
    )
    {
        var drafts = aliases.ToDrafts();
        var requestedAliases = drafts
            .SelectMany(draft => CommandAliasNormalizer.Split(draft.Aliases))
            .ToArray();
        var fixedCollision = FixedChatCommandRoutes.FindCollision(requestedAliases);
        if (fixedCollision is not null)
        {
            return new PointsConfigurationSaveFailure(fixedCollision);
        }

        var ownedKinds = PointsAppCommandKindMap.AppKinds.ToArray();
        var existingCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(alias => requestedAliases.Contains(alias.Alias))
            .Where(alias =>
                alias.HostId == hostId
                && (!ownedKinds.Contains(alias.Kind) || alias.GuessRoundProfileId != null)
            )
            .Select(alias => alias.Alias)
            .FirstOrDefaultAsync(ct);
        if (existingCollision is not null)
        {
            return new PointsConfigurationSaveFailure(existingCollision);
        }

        var customCollision = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(alias => alias.HostId == hostId && requestedAliases.Contains(alias.Alias))
            .Select(alias => alias.Alias)
            .FirstOrDefaultAsync(ct);
        if (customCollision is not null)
        {
            return new PointsConfigurationSaveFailure(customCollision);
        }

        db.CommandAliases.RemoveRange(
            db.CommandAliases.Where(alias =>
                alias.HostId == hostId
                && ownedKinds.Contains(alias.Kind)
                && alias.GuessRoundProfileId == null
            )
        );
        db.CommandAliases.AddRange(
            drafts.SelectMany(draft =>
                CommandAliasNormalizer
                    .Split(draft.Aliases)
                    .Select(alias => new CommandAlias
                    {
                        HostId = hostId,
                        GuessRoundProfileId = null,
                        Kind = draft.Kind,
                        Alias = alias,
                    })
            )
        );
        return null;
    }
}
