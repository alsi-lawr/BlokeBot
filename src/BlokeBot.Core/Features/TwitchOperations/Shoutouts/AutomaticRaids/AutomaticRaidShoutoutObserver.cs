using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

public sealed class AutomaticRaidShoutoutObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IAutomaticRaidShoutoutDelivery delivery,
    TimeProvider clock
) : IIncomingRaidEventObserver
{
    internal static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan ClaimContentionRetryDelay = TimeSpan.FromMilliseconds(25);
    internal const int ClaimContentionCommandTimeoutSeconds = 1;
    internal const int ClaimContentionMaximumAttempts = 3;
    internal const int TerminalOutcomeRetention = 100;

    public async Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    )
    {
        if (!HasUsableIdentity(incomingRaid))
        {
            return;
        }

        var now = clock.GetUtcNow();
        if (now - incomingRaid.MessageTimestamp > FreshnessWindow)
        {
            return;
        }

        await using var lookup = await dbFactory.CreateDbContextAsync(cancellationToken);
        var normalizedTargetLogin = Login.Normalize(incomingRaid.ToBroadcasterUserLogin);
        var host = !string.IsNullOrWhiteSpace(incomingRaid.ToBroadcasterUserId)
            ? await lookup
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.TwitchUserId == incomingRaid.ToBroadcasterUserId,
                    cancellationToken
                )
            : await lookup
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Login == normalizedTargetLogin,
                    cancellationToken
                );
        if (host is null)
        {
            return;
        }
        var settings = await lookup
            .AutomaticRaidShoutoutSettings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.HostId == host.Id, cancellationToken);
        var configuration = settings is null
            ? AutomaticRaidShoutoutConfiguration.Defaults
            : AutomaticRaidShoutoutConfigurationService.Map(settings);
        if (
            !host.EnabledFeatures.Contains(HostFeatureFlags.NativeTwitch)
            || !configuration.Enabled
            || AutomaticRaidShoutoutConfigurationService.Validate(configuration).Count > 0
            || incomingRaid.ViewerCount < configuration.MinimumViewerCount
        )
        {
            return;
        }

        var outcomeId = await TryClaimAsync(host.Id, incomingRaid, now, cancellationToken);
        if (outcomeId is null)
        {
            return;
        }

        var result = await delivery.DeliverAsync(
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
        await StoreResultAsync(host.Id, outcomeId.Value, result, cancellationToken);
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
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                ((SqliteConnection)db.Database.GetDbConnection()).DefaultTimeout =
                    ClaimContentionCommandTimeoutSeconds;
                db.Database.SetCommandTimeout(ClaimContentionCommandTimeoutSeconds);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                await db.Database.ExecuteSqlInterpolatedAsync(
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
                db.AutomaticRaidShoutoutOutcomes.Add(outcome);
                await db.SaveChangesAsync(cancellationToken);
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

    private async Task StoreResultAsync(
        int hostId,
        long outcomeId,
        AutomaticRaidShoutoutDeliveryResult result,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var outcome = await db.AutomaticRaidShoutoutOutcomes.SingleAsync(
            value => value.Id == outcomeId && value.HostId == hostId,
            cancellationToken
        );
        if (outcome.Status is not AutomaticRaidShoutoutOutcomeStatus.Processing)
        {
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        switch (result)
        {
            case AutomaticRaidShoutoutDeliveryResult.Delivered:
                outcome.Status = AutomaticRaidShoutoutOutcomeStatus.Delivered;
                outcome.ResultCode = AutomaticRaidShoutoutResultCode.Delivered;
                break;
            case AutomaticRaidShoutoutDeliveryResult.Ambiguous:
                outcome.Status = AutomaticRaidShoutoutOutcomeStatus.Ambiguous;
                outcome.ResultCode = AutomaticRaidShoutoutResultCode.Ambiguous;
                break;
            case AutomaticRaidShoutoutDeliveryResult.NotDelivered notDelivered:
                outcome.Status = AutomaticRaidShoutoutOutcomeStatus.NotDelivered;
                outcome.ResultCode = notDelivered.Reason
                    is AutomaticRaidShoutoutResultCode.Delivered
                        or AutomaticRaidShoutoutResultCode.Ambiguous
                    ? AutomaticRaidShoutoutResultCode.Unexpected
                    : notDelivered.Reason;
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported automatic shoutout delivery result."
                );
        }
        outcome.CompletedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        await db
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
    }

    private static bool HasUsableIdentity(EventSubIncomingRaidEvent incomingRaid)
    {
        return !string.IsNullOrWhiteSpace(incomingRaid.MessageId)
            && incomingRaid.MessageId.Length <= 128
            && incomingRaid.MessageTimestamp != default
            && !string.IsNullOrWhiteSpace(incomingRaid.FromBroadcasterUserId)
            && incomingRaid.FromBroadcasterUserId.Length <= 64
            && !string.IsNullOrWhiteSpace(Login.Normalize(incomingRaid.FromBroadcasterUserLogin))
            && Login.Normalize(incomingRaid.FromBroadcasterUserLogin).Length <= 128
            && incomingRaid.FromBroadcasterUserName.Length <= 128
            && incomingRaid.ToBroadcasterUserId.Length <= 64
            && Login.Normalize(incomingRaid.ToBroadcasterUserLogin).Length <= 128
            && (
                !string.IsNullOrWhiteSpace(incomingRaid.ToBroadcasterUserId)
                || !string.IsNullOrWhiteSpace(Login.Normalize(incomingRaid.ToBroadcasterUserLogin))
            )
            && incomingRaid.ViewerCount >= 0;
    }

    private static bool IsPersistenceContention(Exception exception)
    {
        return exception switch
        {
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
            } => true,
            DbUpdateException { InnerException: { } inner } => IsPersistenceContention(inner),
            _ => false,
        };
    }
}
