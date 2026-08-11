using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.BlokeRaid;

/// <summary>
/// The supported guessing integration. A completed round contributes once, after guessing commits,
/// and awards the configured damage to each distinct correct recorded login. The persisted round ID
/// is the idempotency key, so notifier retries and process restarts cannot repeat its effect.
/// </summary>
public interface IBlokeRaidGuessingIntegration
{
    Task ProcessCompletedRoundAsync(int hostId, int roundId, CancellationToken cancellationToken);
}

internal sealed class BlokeRaidRuntime(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BlokeRaidService raids,
    ILogger<BlokeRaidRuntime> log
) : IGuessingChangeObserver, IBlokeRaidGuessingIntegration
{
    public async ValueTask GuessingChangedAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Id == hostId
                && (value.EnabledFeatures & HostFeatureFlags.CooperativeGame)
                    == HostFeatureFlags.CooperativeGame
            )
            .Select(value => new { value.BlokeRaidAcceptWorkAfterUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (host is null)
        {
            return;
        }

        var acceptAfter = host.BlokeRaidAcceptWorkAfterUtc ?? DateTime.MinValue;
        var roundIds = await db
            .Rounds.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Status == GuessRoundStatus.Completed
                && value.WinningName != null
                && value.ClosedAtUtc != null
                && value.ClosedAtUtc >= acceptAfter
            )
            .OrderByDescending(value => value.Id)
            .Take(100)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var roundId in roundIds)
        {
            await ProcessCompletedRoundAsync(hostId, roundId, cancellationToken);
        }
    }

    public async Task ProcessCompletedRoundAsync(
        int hostId,
        int roundId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var round = await db
                .Rounds.AsNoTracking()
                .Where(value =>
                    value.HostId == hostId
                    && value.Id == roundId
                    && value.Status == GuessRoundStatus.Completed
                    && value.WinningName != null
                    && value.ClosedAtUtc != null
                )
                .Select(value => new
                {
                    value.Id,
                    value.WinningName,
                    value.ClosedAtUtc,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (round is null)
            {
                return;
            }

            var correctLogins = await db
                .Votes.AsNoTracking()
                .Where(value =>
                    value.GuessRoundId == round.Id && value.GuessName == round.WinningName
                )
                .OrderBy(value => value.GuessedAtUtc)
                .ThenBy(value => value.Id)
                .Select(value => value.Login)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            if (correctLogins.Length == 0)
            {
                return;
            }

            _ = await raids.ApplyGuessingResultAsync(
                hostId,
                new(
                    round.Id,
                    new DateTimeOffset(
                        DateTime.SpecifyKind(round.ClosedAtUtc!.Value, DateTimeKind.Utc)
                    ),
                    [
                        .. correctLogins.Select(login => new BlokeRaidViewer(
                            $"login:{CommunityInput.NormalizeLogin(login)}",
                            CommunityInput.NormalizeLogin(login),
                            login
                        )),
                    ]
                ),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogWarning(
                exception,
                "BlokeRaid could not apply completed guessing round {RoundId} for host {HostId}.",
                roundId,
                hostId
            );
        }
    }
}
