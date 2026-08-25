using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

public sealed class AutomaticRaidShoutoutRunner
{
    private readonly IDbContextFactory<BlokeBotDbContext> _dbFactory;
    private readonly IAutomaticRaidShoutoutDelivery _delivery;
    private readonly AutomaticRaidShoutoutOutcomeAuthority _outcomes;
    private readonly TimeProvider _clock;

    internal AutomaticRaidShoutoutRunner(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        IAutomaticRaidShoutoutDelivery delivery,
        AutomaticRaidShoutoutOutcomeAuthority outcomes,
        TimeProvider clock
    )
    {
        _dbFactory = dbFactory;
        _delivery = delivery;
        _outcomes = outcomes;
        _clock = clock;
    }

    internal static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan ClaimContentionRetryDelay = TimeSpan.FromMilliseconds(25);
    internal const int ClaimContentionCommandTimeoutSeconds = 1;
    internal const int ClaimContentionMaximumAttempts = 3;
    internal const int TerminalOutcomeRetention = 100;

    public async Task<AutomaticRaidShoutoutResultCode?> RunAsync(
        BotHost host,
        AutomaticRaidShoutoutConfiguration configuration,
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    )
    {
        var now = _clock.GetUtcNow();
        if (
            !configuration.Enabled
            || !HasUsableIdentity(incomingRaid)
            || now - incomingRaid.MessageTimestamp > FreshnessWindow
            || configuration.Validate().Count > 0
            || incomingRaid.ViewerCount < configuration.MinimumViewerCount
        )
        {
            return null;
        }

        var outcomeId = await TryClaimAsync(host.Id, incomingRaid, now, cancellationToken);
        if (outcomeId is null)
        {
            return null;
        }

        var result = await _delivery.DeliverAsync(
            new AutomaticRaidShoutoutDeliveryRequest(
                host.Id,
                host.Login,
                configuration,
                incomingRaid.MessageId,
                incomingRaid.MessageTimestamp,
                incomingRaid.FromBroadcasterUserId,
                Login.Normalize(incomingRaid.FromBroadcasterUserLogin),
                incomingRaid.FromBroadcasterUserName,
                incomingRaid.ViewerCount
            ),
            cancellationToken
        );
        return await StoreResultAsync(host.Id, outcomeId.Value, result, cancellationToken);
    }

    private async Task<long?> TryClaimAsync(
        int hostId,
        EventSubIncomingRaidEvent incomingRaid,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                ((SqliteConnection)db.Database.GetDbConnection()).DefaultTimeout =
                    ClaimContentionCommandTimeoutSeconds;
                db.Database.SetCommandTimeout(ClaimContentionCommandTimeoutSeconds);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                _ = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM automatic_raid_processed_events WHERE ExpiresAtUtc < {now.UtcDateTime};",
                    cancellationToken
                );
                var expiry = incomingRaid.MessageTimestamp.Add(FreshnessWindow);
                var changed = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT OR IGNORE INTO automatic_raid_processed_events
                        (HostId, ProviderMessageId, ClaimedAtUtc, ExpiresAtUtc)
                    VALUES
                        ({hostId}, {incomingRaid.MessageId}, {now.UtcDateTime}, {expiry.UtcDateTime});
                    """,
                    cancellationToken
                );
                if (changed != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }

                var outcome = new AutomaticRaidShoutoutOutcome
                {
                    HostId = hostId,
                    ProviderMessageId = incomingRaid.MessageId,
                    SourceTwitchUserId = incomingRaid.FromBroadcasterUserId,
                    SourceLogin = Login.Normalize(incomingRaid.FromBroadcasterUserLogin),
                    SourceDisplayName = incomingRaid.FromBroadcasterUserName,
                    ViewerCount = incomingRaid.ViewerCount,
                    Status = AutomaticRaidShoutoutOutcomeStatus.Processing,
                    MessageTimestampUtc = incomingRaid.MessageTimestamp.UtcDateTime,
                    ClaimedAtUtc = now.UtcDateTime,
                };
                _ = db.AutomaticRaidShoutoutOutcomes.Add(outcome);
                _ = await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return outcome.Id;
            }
            catch (Exception exception)
                when (IsPersistenceContention(exception) && attempt < ClaimContentionMaximumAttempts
                )
            {
                await Task.Delay(ClaimContentionRetryDelay, cancellationToken);
            }
        }
    }

    private async Task<AutomaticRaidShoutoutResultCode?> StoreResultAsync(
        int hostId,
        long outcomeId,
        AutomaticRaidShoutoutDeliveryResult result,
        CancellationToken cancellationToken
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var providerMessageId = await db
            .AutomaticRaidShoutoutOutcomes.Where(value =>
                value.Id == outcomeId && value.HostId == hostId
            )
            .Select(value => value.ProviderMessageId)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerMessageId is null)
        {
            return null;
        }

        AutomaticRaidOutcomeTransition transition = result switch
        {
            AutomaticRaidShoutoutDeliveryResult.Queued =>
                new AutomaticRaidOutcomeTransition.QueueAccepted(),
            AutomaticRaidShoutoutDeliveryResult.Delivered =>
                new AutomaticRaidOutcomeTransition.TransportDelivered(),
            AutomaticRaidShoutoutDeliveryResult.Ambiguous =>
                new AutomaticRaidOutcomeTransition.Ambiguous(),
            AutomaticRaidShoutoutDeliveryResult.NotDelivered notDelivered =>
                new AutomaticRaidOutcomeTransition.TerminalFailure(notDelivered.Reason),
            _ => throw new InvalidOperationException(
                "Unsupported automatic shoutout delivery result."
            ),
        };
        var stored = await _outcomes.ApplyAsync(
            db,
            new AutomaticRaidOutcomeIdentity(hostId, outcomeId, providerMessageId),
            transition,
            _clock.GetUtcNow(),
            cancellationToken
        );

        _ = await db
            .AutomaticRaidShoutoutOutcomes.Where(value =>
                value.HostId == hostId
                && (
                    value.Status == AutomaticRaidShoutoutOutcomeStatus.Delivered
                    || value.Status == AutomaticRaidShoutoutOutcomeStatus.NotDelivered
                )
            )
            .OrderByDescending(value => value.CompletedAtUtc)
            .ThenByDescending(value => value.Id)
            .Skip(TerminalOutcomeRetention)
            .ExecuteDeleteAsync(cancellationToken);
        return stored switch
        {
            AutomaticRaidOutcomeTransitionResult.Applied applied => applied.State.ResultCode,
            AutomaticRaidOutcomeTransitionResult.Unchanged unchanged => unchanged.State.ResultCode,
            AutomaticRaidOutcomeTransitionResult.NotFound => null,
            _ => null,
        };
    }

    private static bool HasUsableIdentity(EventSubIncomingRaidEvent incomingRaid) =>
        !string.IsNullOrWhiteSpace(incomingRaid.MessageId)
        && incomingRaid.MessageId.Length <= 128
        && incomingRaid.MessageTimestamp != default
        && !string.IsNullOrWhiteSpace(incomingRaid.FromBroadcasterUserId)
        && incomingRaid.FromBroadcasterUserId.Length <= 64
        && !string.IsNullOrWhiteSpace(Login.Normalize(incomingRaid.FromBroadcasterUserLogin))
        && Login.Normalize(incomingRaid.FromBroadcasterUserLogin).Length <= 128
        && incomingRaid.FromBroadcasterUserName.Length <= 128
        && incomingRaid.ToBroadcasterUserId.Length <= 64
        && Login.Normalize(incomingRaid.ToBroadcasterUserLogin).Length <= 128
        && incomingRaid.ViewerCount >= 0;

    private static bool IsPersistenceContention(Exception exception) =>
        exception switch
        {
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
            } => true,
            DbUpdateException { InnerException: { } inner } => IsPersistenceContention(inner),
            _ => false,
        };
}
