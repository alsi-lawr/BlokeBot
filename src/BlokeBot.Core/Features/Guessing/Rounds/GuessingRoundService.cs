using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public sealed class GuessingRoundService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes,
    PointBalanceService balances,
    PointsChangeNotifier pointsChanges
)
{
    public IO<GuessingWinnerDeclarationOutcome, Never> DeclareWinner(int hostId, string name)
    {
        return IO<GuessingWinnerDeclarationOutcome, Never>.Create(async ct =>
            Result<GuessingWinnerDeclarationOutcome, Never>.Success(
                await DeclareWinnerCoreAsync(hostId, name, ct)
            )
        );
    }

    private async Task<GuessingWinnerDeclarationOutcome> DeclareWinnerCoreAsync(
        int hostId,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await GuessingRoundQueries.LoadTrackedUnresolvedAsync(db, hostId, ct);
        var resolution = round is null
            ? await GuessingReplySettingsQueries.LoadForDefaultAsync(db, hostId, ct)
            : await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId,
                round.GuessRoundProfileId,
                ct
            );
        var settings = resolution.Settings;
        var delivery = resolution.ReplyDelivery;
        var normalizedName = GuessName.Parse(name).Value;

        if (round is null)
        {
            return Completed(
                new GuessingOperationOutcome.Rejected(
                    settings.NoOpenRoundReply,
                    delivery.TargetFor(GuessingReplyKeys.NoOpenRound)
                )
            );
        }

        var optionExists = await db.GuessOptions.AnyAsync(
            x => x.GuessRoundProfileId == round.GuessRoundProfileId && x.Name == normalizedName,
            ct
        );
        if (!optionExists)
        {
            return Completed(
                new GuessingOperationOutcome.Rejected(
                    Format(settings.InvalidGuessReply, normalizedName, string.Empty),
                    delivery.TargetFor(GuessingReplyKeys.InvalidGuess)
                )
            );
        }

        var winners = await db
            .Votes.AsNoTracking()
            .Where(x => x.GuessRoundId == round.Id && x.GuessName == normalizedName)
            .OrderBy(x => x.GuessedAtUtc)
            .Select(x => x.Login)
            .ToListAsync(ct);
        var reward = await db
            .Profiles.AsNoTracking()
            .Where(x => x.Id == round.GuessRoundProfileId)
            .Select(x => x.WinningGuessPointReward)
            .SingleAsync(ct);
        var rewardAmount = PointAmount.ParseAbsolute(reward);
        var pointLabel =
            await db
                .PointsSettings.AsNoTracking()
                .Where(x => x.HostId == hostId)
                .Select(x => x.PointLabel)
                .SingleOrDefaultAsync(ct)
            ?? "points";
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        round.Status = GuessRoundStatus.Completed;
        round.ClosedAtUtc ??= now;
        round.WinningName = normalizedName;
        Result<List<PointBalanceMutation>, PointBalanceMutationFailure> payoutAttempt = Result<
            List<PointBalanceMutation>,
            PointBalanceMutationFailure
        >.Success([]);
        if (!rewardAmount.IsZero)
        {
            foreach (var winner in winners)
            {
                payoutAttempt = await payoutAttempt.Match(
                    async mutations =>
                    {
                        var result = await balances
                            .AwardGuessWin(db, hostId, round.Id, winner, rewardAmount, now)
                            .ExecuteAsync(ct);
                        return result.Match(
                            mutation =>
                            {
                                mutations.Add(mutation);
                                return Result<
                                    List<PointBalanceMutation>,
                                    PointBalanceMutationFailure
                                >.Success(mutations);
                            },
                            Result<List<PointBalanceMutation>, PointBalanceMutationFailure>.Error
                        );
                    },
                    failure =>
                        Task.FromResult(
                            Result<List<PointBalanceMutation>, PointBalanceMutationFailure>.Error(
                                failure
                            )
                        )
                );
            }
        }

        return await payoutAttempt.Match(CommitAsync, PayoutFailedAsync);

        async Task<GuessingWinnerDeclarationOutcome> CommitAsync(
            List<PointBalanceMutation> mutations
        )
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await changes.NotifyChangedAsync(ct);
            if (mutations.Count > 0)
            {
                await pointsChanges.NotifyChangedAsync(ct);
            }

            var message = MessageTemplateFormatter.Format(
                winners.Count == 0 ? settings.NoWinnersReply : settings.WinnerReply,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = normalizedName,
                    ["winners"] = winners.Count == 0 ? "none" : string.Join(", ", winners),
                    ["count"] = winners.Count.ToString(),
                    ["reward"] = rewardAmount.ToDisplayString(),
                    ["label"] = pointLabel,
                    ["reward_text"] =
                        rewardAmount.IsZero || winners.Count == 0
                            ? string.Empty
                            : $" Each winner gets {rewardAmount.ToDisplayString()} {pointLabel}.",
                }
            );
            return Completed(new GuessingOperationOutcome.Succeeded(message));
        }

        static Task<GuessingWinnerDeclarationOutcome> PayoutFailedAsync(
            PointBalanceMutationFailure failure
        )
        {
            return Task.FromResult<GuessingWinnerDeclarationOutcome>(
                new GuessingWinnerDeclarationOutcome.PayoutFailed(failure)
            );
        }
    }

    public IO<GuessingWinnerDeclarationOutcome, Never> DeclareWinner(string hostLogin, string name)
    {
        return IO<GuessingWinnerDeclarationOutcome, Never>.Create(async ct =>
            Result<GuessingWinnerDeclarationOutcome, Never>.Success(
                await DeclareWinnerByLoginAsync(hostLogin, name, ct)
            )
        );
    }

    private async Task<GuessingWinnerDeclarationOutcome> DeclareWinnerByLoginAsync(
        string hostLogin,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = (await ResolveHostIdAsync(db, hostLogin, ct)).Match<int?>(
            value => value,
            () => null
        );
        return hostId is null
            ? Completed(NotConfigured())
            : await DeclareWinnerCoreAsync(hostId.Value, name, ct);
    }

    public IO<GuessingOperationOutcome, Never> StartRound(int hostId, int profileId)
    {
        return IO<GuessingOperationOutcome, Never>.Create(async ct =>
            Result<GuessingOperationOutcome, Never>.Success(
                await StartRoundCoreAsync(hostId, profileId, ct)
            )
        );
    }

    private async Task<GuessingOperationOutcome> StartRoundCoreAsync(
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var profile = await db.Profiles.LoadProfileWithOptionsAsync(hostId, profileId, ct);
        if (profile is null)
        {
            return new GuessingOperationOutcome.Rejected("Round type not found.");
        }

        var settings = profile.Settings;
        var delivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyFeature.Guessing,
            profile.Id,
            ct
        );
        if (await GuessingRoundQueries.HasUnresolvedAsync(db, hostId, ct))
        {
            return new GuessingOperationOutcome.Rejected(
                settings.RoundAlreadyOpenReply,
                delivery.TargetFor(GuessingReplyKeys.RoundAlreadyOpen)
            );
        }

        var round = new GuessRound
        {
            HostId = hostId,
            GuessRoundProfileId = profile.Id,
            Status = GuessRoundStatus.Open,
            StartedAtUtc = DateTime.UtcNow,
        };
        db.Rounds.Add(round);
        await db.SaveChangesAsync(ct);
        var pinPolicy = await db
            .ReplyPinPolicies.AsNoTracking()
            .SingleOrDefaultAsync(
                policy =>
                    policy.HostId == hostId
                    && policy.Feature == "guessing"
                    && policy.ReplyKey == GuessingReplyKeys.RoundStarted,
                ct
            );
        await changes.NotifyChangedAsync(ct);
        return new GuessingOperationOutcome.Succeeded(
            FormatRoundStarted(
                settings.RoundStartedReply,
                profile.Name,
                FormatOptions(profile.OptionNames)
            ),
            PublicChatPin: pinPolicy is null
                ? null
                : new PublicChatPinIntent(
                    hostId,
                    round.Id,
                    "guessing",
                    GuessingReplyKeys.RoundStarted,
                    pinPolicy.DurationSeconds,
                    pinPolicy.UnpinOnOwnerCompletion
                )
        );
    }

    public IO<GuessingOperationOutcome, Never> StartRound(string hostLogin, string? profileName)
    {
        return IO<GuessingOperationOutcome, Never>.Create(async ct =>
            Result<GuessingOperationOutcome, Never>.Success(
                await StartRoundByLoginAsync(hostLogin, profileName, ct)
            )
        );
    }

    private async Task<GuessingOperationOutcome> StartRoundByLoginAsync(
        string hostLogin,
        string? profileName,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = (await ResolveHostIdAsync(db, hostLogin, ct)).Match<int?>(
            value => value,
            () => null
        );
        if (hostId is null)
        {
            return NotConfigured();
        }

        var profileId = string.IsNullOrWhiteSpace(profileName)
            ? await db.Profiles.LoadDefaultProfileIdAsync(hostId.Value, ct)
            : await db.Profiles.LoadProfileIdByNameAsync(hostId.Value, profileName, ct);

        if (profileId is null)
        {
            return new GuessingOperationOutcome.Rejected($"Unknown round type: {profileName}.");
        }

        return await StartRoundCoreAsync(hostId.Value, profileId.Value, ct);
    }

    public IO<GuessingOperationOutcome, Never> StopGuessing(int hostId)
    {
        return IO<GuessingOperationOutcome, Never>.Create(async ct =>
            Result<GuessingOperationOutcome, Never>.Success(await StopGuessingCoreAsync(hostId, ct))
        );
    }

    private async Task<GuessingOperationOutcome> StopGuessingCoreAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var round = await GuessingRoundQueries.LoadTrackedOpenAsync(db, hostId, ct);
        var settingsRound =
            round ?? await GuessingRoundQueries.LoadTrackedUnresolvedAsync(db, hostId, ct);
        var resolution = settingsRound is null
            ? await GuessingReplySettingsQueries.LoadForDefaultAsync(db, hostId, ct)
            : await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId,
                settingsRound.GuessRoundProfileId,
                ct
            );
        var settings = resolution.Settings;
        var delivery = resolution.ReplyDelivery;

        if (round is null)
        {
            return new GuessingOperationOutcome.Rejected(
                settings.NoOpenRoundReply,
                delivery.TargetFor(GuessingReplyKeys.NoOpenRound)
            );
        }

        round.Status = GuessRoundStatus.Closed;
        round.ClosedAtUtc = DateTime.UtcNow;
        await db
            .PublicChatPinOperations.Where(operation =>
                operation.HostId == hostId
                && operation.Feature == "guessing"
                && operation.OwnerId == round.Id
                && (
                    operation.Status == PublicChatPinOperationStatus.AwaitingDelivery
                    || operation.Status == PublicChatPinOperationStatus.Ready
                )
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(
                            operation => operation.Status,
                            PublicChatPinOperationStatus.Terminal
                        )
                        .SetProperty(operation => operation.OutboxMessageId, (long?)null)
                        .SetProperty(operation => operation.CompletedAtUtc, DateTime.UtcNow)
                        .SetProperty(operation => operation.Outcome, "owner-completed-before-pin"),
                ct
            );
        var activePin = await db.ActivePublicChatPins.SingleOrDefaultAsync(
            pin =>
                pin.HostId == hostId
                && pin.Feature == "guessing"
                && pin.OwnerId == round.Id
                && pin.UnpinOnOwnerCompletion,
            ct
        );
        if (activePin is not null)
        {
            db.PublicChatPinOperations.Add(
                new PublicChatPinOperation
                {
                    Kind = PublicChatPinOperationKind.Unpin,
                    Status = PublicChatPinOperationStatus.Ready,
                    HostId = hostId,
                    Channel = activePin.Channel,
                    Feature = activePin.Feature,
                    ReplyKey = activePin.ReplyKey,
                    OwnerId = round.Id,
                    TwitchMessageId = activePin.TwitchMessageId,
                    PinnerTwitchUserId = activePin.PinnerTwitchUserId,
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
        }
        else
        {
            var attemptingPin = await db
                .PublicChatPinOperations.AsNoTracking()
                .FirstOrDefaultAsync(
                    operation =>
                        operation.HostId == hostId
                        && operation.Feature == "guessing"
                        && operation.OwnerId == round.Id
                        && operation.Kind == PublicChatPinOperationKind.Pin
                        && operation.Status == PublicChatPinOperationStatus.Attempting,
                    ct
                );
            if (attemptingPin is not null)
            {
                db.PublicChatPinOperations.Add(
                    new PublicChatPinOperation
                    {
                        Kind = PublicChatPinOperationKind.Unpin,
                        Status = PublicChatPinOperationStatus.Ready,
                        HostId = hostId,
                        Channel = attemptingPin.Channel,
                        Feature = attemptingPin.Feature,
                        ReplyKey = attemptingPin.ReplyKey,
                        OwnerId = round.Id,
                        TwitchMessageId = attemptingPin.TwitchMessageId,
                        CreatedAtUtc = DateTime.UtcNow,
                    }
                );
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return new GuessingOperationOutcome.Succeeded(settings.GuessingStoppedReply);
    }

    public IO<GuessingOperationOutcome, Never> StopGuessing(string hostLogin)
    {
        return IO<GuessingOperationOutcome, Never>.Create(async ct =>
            Result<GuessingOperationOutcome, Never>.Success(
                await StopGuessingByLoginAsync(hostLogin, ct)
            )
        );
    }

    private async Task<GuessingOperationOutcome> StopGuessingByLoginAsync(
        string hostLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = (await ResolveHostIdAsync(db, hostLogin, ct)).Match<int?>(
            value => value,
            () => null
        );
        return hostId is null ? NotConfigured() : await StopGuessingCoreAsync(hostId.Value, ct);
    }

    private static string Format(string template, string name, string login)
    {
        return MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["login"] = login,
            }
        );
    }

    private static string FormatRoundStarted(string template, string round, string options)
    {
        return MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["round"] = round,
                ["options"] = options,
            }
        );
    }

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static GuessingOperationOutcome NotConfigured()
    {
        return new GuessingOperationOutcome.Rejected("This channel is not set up.");
    }

    private static GuessingWinnerDeclarationOutcome Completed(GuessingOperationOutcome result)
    {
        return new GuessingWinnerDeclarationOutcome.Completed(result);
    }

    private static ValueTask<Option<int>> ResolveHostIdAsync(
        BlokeBotDbContext db,
        string hostLogin,
        CancellationToken ct
    )
    {
        return BotHostQueries.FindHostId(db, hostLogin).RunAsync(ct);
    }
}
