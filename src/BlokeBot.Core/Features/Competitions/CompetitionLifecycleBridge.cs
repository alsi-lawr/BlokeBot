using BlokeBot.Core.Features.Automations;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Competitions;

public interface ICompetitionLifecycleAutomationDispatcher
{
    Task DispatchAsync(
        CompetitionLifecycleEvent competitionEvent,
        BotHost host,
        CancellationToken cancellationToken
    );
}

public sealed class CompetitionLifecycleAutomationDispatcher(
    IServiceProvider services,
    TimeProvider clock
) : ICompetitionLifecycleAutomationDispatcher
{
    public async Task DispatchAsync(
        CompetitionLifecycleEvent competitionEvent,
        BotHost host,
        CancellationToken cancellationToken
    )
    {
        var context = new AutomationContext(
            new(competitionEvent.OccurrenceId, CompetitionAutomationDefinitionIds.LifecycleSource),
            null,
            new(
                new(host.Id),
                host.TwitchUserId ?? string.Empty,
                host.Login,
                string.IsNullOrWhiteSpace(host.DisplayName) ? host.Login : host.DisplayName
            ),
            null,
            new(competitionEvent.OccurredAtUtc, clock.GetUtcNow()),
            [],
            new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("event-kind")] = SafeText(competitionEvent.Kind.ToString()),
                    [new("competition-id")] = SafeText(
                        competitionEvent.CompetitionId.Value.ToString("N")
                    ),
                    [new("public-payload")] = SafeText(competitionEvent.PublicPayload),
                }
            )
        );
        // Runtime resolution belongs at the committed-event boundary; eager construction would
        // cycle through HostFeatureService's competition-disable observer back into this bridge.
        _ = await services
            .GetRequiredService<AutomationRuntimeService>()
            .DispatchAsync(
                new(context, new CompetitionLifecycleSourceConfiguration()),
                cancellationToken
            );
    }

    private static AutomationVariable SafeText(string value) =>
        new(new AutomationValue.Text(value), AutomationDataSensitivity.Safe);
}

public sealed class CompetitionLifecycleBridge(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    ICompetitionLifecycleAutomationDispatcher automations,
    ILogger<CompetitionLifecycleBridge> logger
) : ICompetitionLifecycleObserver
{
    public async ValueTask CompetitionChangedAsync(
        CompetitionLifecycleEvent competitionEvent,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var host = await db
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(
                    value =>
                        value.Id == competitionEvent.HostId
                        && (value.EnabledFeatures & HostFeatureFlags.Competitions)
                            == HostFeatureFlags.Competitions
                        && (
                            value.CompetitionsAcceptWorkAfterUtc == null
                            || competitionEvent.OccurredAtUtc.UtcDateTime
                                >= value.CompetitionsAcceptWorkAfterUtc
                        ),
                    cancellationToken
                );
            if (host is null)
            {
                return;
            }

            _ = await events.PublishAsync(AppEventKind.CompetitionsChanged, cancellationToken);
            if (
                (host.EnabledFeatures & HostFeatureFlags.Automations)
                == HostFeatureFlags.Automations
            )
            {
                await automations.DispatchAsync(competitionEvent, host, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Competition lifecycle bridge failed for host {HostId} and occurrence {OccurrenceId}.",
                competitionEvent.HostId,
                competitionEvent.OccurrenceId
            );
        }
    }
}
