using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bingo;

public interface IBingoCounterEventSink
{
    Task CounterChangedAsync(
        int hostId,
        string invocationId,
        int counterId,
        string counterName,
        long value,
        BingoViewer? viewer,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken
    );
}

internal sealed class BingoRuntime(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BingoService bingo,
    ILogger<BingoRuntime> log
)
    : ITwitchEventAutomationObserver,
        IEventSubRequirementSource,
        IBountyCompletionObserver,
        IGuessingChangeObserver,
        IPointsGiveawayChangeObserver,
        IBingoCounterEventSink
{
    public Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            incomingRaid.ToBroadcasterUserId,
            incomingRaid.ToBroadcasterUserLogin,
            new BingoAutomaticEvent.IncomingRaid(
                incomingRaid.MessageId,
                new(
                    incomingRaid.FromBroadcasterUserId,
                    incomingRaid.FromBroadcasterUserLogin,
                    incomingRaid.FromBroadcasterUserName
                ),
                incomingRaid.ViewerCount,
                incomingRaid.MessageTimestamp
            ),
            cancellation
        );

    public Task ChannelUpdatedAsync(
        EventSubChannelUpdateEvent channelUpdate,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            channelUpdate.BroadcasterUserId,
            channelUpdate.BroadcasterUserLogin,
            new BingoAutomaticEvent.StreamCategoryChanged(
                channelUpdate.MessageId,
                channelUpdate.CategoryId,
                channelUpdate.CategoryName,
                channelUpdate.MessageTimestamp
            ),
            cancellation
        );

    public async Task BountyCompletedAsync(
        int hostId,
        Guid bountyPublicId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken
    ) =>
        await ProcessSafelyAsync(
            hostId,
            new BingoAutomaticEvent.BountyCompleted(bountyPublicId, completedAtUtc),
            cancellationToken
        );

    public async ValueTask GuessingChangedAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var completed = await db
            .Rounds.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Status == GuessRoundStatus.Completed
                && value.WinningName != null
                && value.ClosedAtUtc != null
            )
            .OrderByDescending(value => value.Id)
            .Take(100)
            .Select(value => new
            {
                value.Id,
                value.WinningName,
                value.ClosedAtUtc,
            })
            .ToArrayAsync(cancellationToken);
        foreach (var value in completed)
        {
            await ProcessSafelyAsync(
                hostId,
                new BingoAutomaticEvent.GuessingResult(
                    value.Id,
                    value.WinningName!,
                    null,
                    value.ClosedAtUtc!.Value
                ),
                cancellationToken
            );
        }
    }

    public async ValueTask GiveawayChangedAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var giveaways = await db
            .PointsGiveaways.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.Id)
            .Take(100)
            .Select(value => new { value.Id, value.StartedAtUtc })
            .ToArrayAsync(cancellationToken);
        foreach (var value in giveaways)
        {
            await ProcessSafelyAsync(
                hostId,
                new BingoAutomaticEvent.GiveawayStarted(value.Id, value.StartedAtUtc),
                cancellationToken
            );
        }
    }

    public async Task CounterChangedAsync(
        int hostId,
        string invocationId,
        int counterId,
        string counterName,
        long value,
        BingoViewer? viewer,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken
    ) =>
        await ProcessSafelyAsync(
            hostId,
            new BingoAutomaticEvent.CounterReached(
                invocationId,
                counterId,
                counterName,
                value,
                viewer,
                occurredAtUtc
            ),
            cancellationToken
        );

    public async ValueTask<bool> RequiresAsync(
        string channel,
        AutomationEventSubRequirement requirement,
        CancellationToken cancellation
    )
    {
        if (
            requirement
            is not (
                AutomationEventSubRequirement.ChannelUpdates
                or AutomationEventSubRequirement.IncomingRaids
            )
        )
        {
            return false;
        }
        var login = CommunityInput.NormalizeLogin(channel);
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        return await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                value =>
                    value.Login == login
                    && (value.EnabledFeatures & HostFeatureFlags.Bingo) == HostFeatureFlags.Bingo,
                cancellation
            );
    }

    public Task StreamOnlineAsync(
        EventSubStreamOnlineEvent streamOnline,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task StreamOfflineAsync(
        EventSubStreamOfflineEvent streamOffline,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task FollowReceivedAsync(EventSubFollowEvent follow, CancellationToken cancellation) =>
        Task.CompletedTask;

    public Task SubscriptionReceivedAsync(
        EventSubSubscriptionEvent subscription,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task SubscriptionGiftReceivedAsync(
        EventSubSubscriptionGiftEvent gift,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task CheerReceivedAsync(EventSubCheerEvent cheer, CancellationToken cancellation) =>
        Task.CompletedTask;

    public Task HypeTrainChangedAsync(
        EventSubHypeTrainEvent hypeTrain,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task ChatNotificationReceivedAsync(
        EventSubChatNotificationEvent notification,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task RewardRedemptionReceivedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task ShoutoutOccurredAsync(
        EventSubShoutoutEvent shoutout,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task PollChangedAsync(EventSubPollEvent poll, CancellationToken cancellation) =>
        Task.CompletedTask;

    public Task PredictionChangedAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    private async Task HandleAsync(
        string broadcasterUserId,
        string broadcasterLogin,
        BingoAutomaticEvent sourceEvent,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hostId = !string.IsNullOrWhiteSpace(broadcasterUserId)
            ? await db
                .Hosts.AsNoTracking()
                .Where(value => value.TwitchUserId == broadcasterUserId)
                .Select(value => (int?)value.Id)
                .SingleOrDefaultAsync(cancellationToken)
            : await db
                .Hosts.AsNoTracking()
                .Where(value => value.Login == CommunityInput.NormalizeLogin(broadcasterLogin))
                .Select(value => (int?)value.Id)
                .SingleOrDefaultAsync(cancellationToken);
        if (hostId is { } value)
        {
            await ProcessSafelyAsync(value, sourceEvent, cancellationToken);
        }
    }

    private async Task ProcessSafelyAsync(
        int hostId,
        BingoAutomaticEvent sourceEvent,
        CancellationToken cancellationToken
    )
    {
        try
        {
            _ = await bingo.ProcessEventAsync(hostId, sourceEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(
                "Bingo source {SourceKind} failed with {FailureType}.",
                sourceEvent.Kind,
                exception.GetType().Name
            );
        }
    }
}

internal sealed class BingoFeatureObserver(
    BingoService bingo,
    IEventSubChannelReconciliationTrigger? eventSub = null
) : IHostFeatureActivationObserver
{
    public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (change.Feature != HostFeatureFlags.Bingo)
        {
            return new HostFeatureAutomaticWorkResult.Complete();
        }
        if (eventSub is not null)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }
        if (change.State is HostFeatureActivationState.Enabled)
        {
            await bingo.ReconcilePendingRewardsAsync(change.HostId, cancellationToken);
        }

        return new HostFeatureAutomaticWorkResult.Complete();
    }
}
