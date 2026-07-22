using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed record PublicChatPinWorkItem(
    long Id,
    bool ReconcileOnly,
    bool IsUnpin,
    int HostId,
    string Channel,
    string Feature,
    string ReplyKey,
    long OwnerId,
    string TwitchMessageId,
    int? DurationSeconds,
    bool UnpinOnOwnerCompletion
);

internal abstract record PublicChatPinExecutionOutcome
{
    private PublicChatPinExecutionOutcome() { }

    internal sealed record Pinned(string PinnerTwitchUserId) : PublicChatPinExecutionOutcome;

    internal sealed record Unpinned : PublicChatPinExecutionOutcome;

    internal sealed record NoOp(string Reason) : PublicChatPinExecutionOutcome;

    internal sealed record Terminal(string Reason) : PublicChatPinExecutionOutcome;
}

internal interface IPublicChatPinStore
{
    ValueTask<PublicChatPinWorkItem?> TryClaimAsync(CancellationToken cancellationToken);
    ValueTask CompleteAsync(
        PublicChatPinWorkItem item,
        PublicChatPinExecutionOutcome outcome,
        CancellationToken cancellationToken
    );
}

internal sealed class UnavailablePublicChatPinStore : IPublicChatPinStore
{
    public ValueTask<PublicChatPinWorkItem?> TryClaimAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<PublicChatPinWorkItem?>(null);
    }

    public ValueTask CompleteAsync(
        PublicChatPinWorkItem item,
        PublicChatPinExecutionOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.CompletedTask;
    }
}

internal interface IPublicChatPinProvider
{
    ValueTask<PublicChatPinExecutionOutcome> ExecuteAsync(
        PublicChatPinWorkItem item,
        CancellationToken cancellationToken
    );
}

internal sealed class PublicChatPinWorker(
    IPublicChatPinStore store,
    IPublicChatPinProvider provider,
    TimeProvider timeProvider,
    ILogger<PublicChatPinWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            PublicChatPinWorkItem? item;
            try
            {
                item = await store.TryClaimAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                log.LogWarning(exception, "Could not claim durable public chat pin work.");
                await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, stoppingToken);
                continue;
            }

            if (item is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeProvider, stoppingToken);
                continue;
            }

            PublicChatPinExecutionOutcome outcome;
            try
            {
                outcome = await provider.ExecuteAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                outcome = new PublicChatPinExecutionOutcome.Terminal(
                    $"unexpected:{exception.GetType().Name}"
                );
            }

            try
            {
                await store.CompleteAsync(item, outcome, CancellationToken.None);
            }
            catch (Exception exception)
            {
                log.LogWarning(
                    exception,
                    "Could not record public chat pin operation {OperationId}; it will be reconciled read-only.",
                    item.Id
                );
                continue;
            }
            if (outcome is PublicChatPinExecutionOutcome.Terminal terminal)
            {
                log.LogWarning(
                    "Public chat pin operation {OperationId} ended without retry: {Reason}.",
                    item.Id,
                    terminal.Reason
                );
            }
        }
    }
}
