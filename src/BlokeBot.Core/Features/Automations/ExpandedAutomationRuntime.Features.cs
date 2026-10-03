using BlokeBot.Core.Features.HostedChannels;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class ExpandedAutomationRuntime
{
    internal async Task FeatureAsync(
        int hostId,
        FeatureLifecycleKind kind,
        string featureId,
        string occurrence,
        DateTimeOffset at,
        string publicData,
        CancellationToken ct,
        AutomationActor? actor = null
    )
    {
        var source = kind switch
        {
            FeatureLifecycleKind.GiveawayOpened
            or FeatureLifecycleKind.GiveawayClosed
            or FeatureLifecycleKind.GiveawayWinners =>
                AutomationDefinitionIds.GiveawayLifecycleSource,
            FeatureLifecycleKind.GuessingStarted or FeatureLifecycleKind.GuessingFinished =>
                AutomationDefinitionIds.GuessingLifecycleSource,
            FeatureLifecycleKind.QueueJoined or FeatureLifecycleKind.QueueCalled =>
                AutomationDefinitionIds.QueueLifecycleSource,
        };
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(h => h.Id == hostId, ct);
        if (
            host is null
            || !host.EnabledFeatures.Contains(
                AutomationRequiredFeatures.ForDefinitions([source.Value])
            )
        )
        {
            return;
        }
        _ = await runtime.DispatchExpandedAsync(
            Create(
                host,
                source,
                $"{occurrence}:{kind}",
                at,
                clock.GetUtcNow(),
                [
                    Text("event-kind", kind.ToString()),
                    Text("feature-id", featureId),
                    Text("public-data", publicData),
                ],
                actor
            ),
            c => c is FeatureLifecycleSourceConfiguration s && s.Event == kind,
            ct,
            deferExecution: true
        );
    }
}

public sealed class AutomationFeatureLifecycle(
    IServiceProvider services,
    ILogger<AutomationFeatureLifecycle> logger
)
{
    internal async Task EmitAsync(
        int hostId,
        FeatureLifecycleKind kind,
        string featureId,
        string occurrence,
        DateTime at,
        string publicData,
        CancellationToken ct,
        AutomationActor? actor = null
    )
    {
        try
        {
            await services
                .GetRequiredService<ExpandedAutomationRuntime>()
                .FeatureAsync(
                    hostId,
                    kind,
                    featureId,
                    occurrence,
                    new(DateTime.SpecifyKind(at, DateTimeKind.Utc)),
                    publicData,
                    ct,
                    actor
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(
                "Committed feature lifecycle automation for host {HostId} failed ({FailureType}).",
                hostId,
                failure.GetType().Name
            );
        }
    }
}
