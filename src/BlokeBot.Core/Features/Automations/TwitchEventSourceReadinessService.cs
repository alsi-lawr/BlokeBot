using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public abstract record TwitchEventSourceReadinessState
{
    private TwitchEventSourceReadinessState() { }

    public sealed record Ready : TwitchEventSourceReadinessState;

    public sealed record MissingScopes(ImmutableArray<string> Scopes)
        : TwitchEventSourceReadinessState;

    public sealed record BroadcasterNotConnected : TwitchEventSourceReadinessState;
}

public sealed record TwitchEventSourceReadiness(
    AutomationDefinitionId DefinitionId,
    string Name,
    string Description,
    string SubscriptionTypes,
    ImmutableArray<string> RequiredBroadcasterScopes,
    bool UsedByEnabledFlow,
    TwitchEventSourceReadinessState State
);

public abstract record TwitchEventSourceReadinessOutcome
{
    private TwitchEventSourceReadinessOutcome() { }

    public sealed record Available(
        ImmutableArray<TwitchEventSourceReadiness> Sources,
        ImmutableArray<string> MissingBroadcasterScopes,
        bool BroadcasterConnected
    ) : TwitchEventSourceReadinessOutcome;

    public sealed record FeatureDisabled : TwitchEventSourceReadinessOutcome;

    public sealed record HostNotFound : TwitchEventSourceReadinessOutcome;
}

/// <summary>
/// Computes the editor-facing readiness of every Twitch EventSub automation source: whether the
/// milestone-wide broadcaster grant is ready, which exact scopes are missing, and whether an
/// enabled flow currently uses the source. There is no per-source consent model; a grant missing
/// any scope of the extended milestone union is not ready until the owner reconnects.
/// </summary>
public sealed class TwitchEventSourceReadinessService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCatalogService catalog,
    AutomationRuntimeService runtime,
    IHostBroadcasterTokenStatusProvider broadcasterTokens
)
{
    public async Task<TwitchEventSourceReadinessOutcome> LoadAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == hostId.Value, cancellationToken);
        if (host is null)
        {
            return new TwitchEventSourceReadinessOutcome.HostNotFound();
        }

        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return new TwitchEventSourceReadinessOutcome.FeatureDisabled();
        }

        var snapshot = await catalog.DiscoverAsync(hostId, cancellationToken);
        var descriptors = snapshot.Definitions.ToImmutableDictionary(static value => value.Id);
        var enabledSources = await runtime.EnabledSourceDefinitionIdsAsync(
            hostId,
            cancellationToken
        );
        var tokenStatus = await broadcasterTokens.GetTokenStatusAsync(
            hostId.Value,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            cancellationToken
        );
        var (broadcasterConnected, missingScopes) = tokenStatus.Match<(
            bool,
            ImmutableArray<string>
        )>(
            static _ => (false, []),
            static _ => (false, []),
            static _ => (false, []),
            static missing => (true, missing.Missing),
            static _ => (true, [])
        );
        var sources = TwitchEventAutomationSources
            .All.Where(source => descriptors.ContainsKey(source.DefinitionId))
            .Select(source =>
            {
                var descriptor = descriptors[source.DefinitionId];
                var missingForSource = source
                    .BroadcasterScopes.Where(scope =>
                        missingScopes.Contains(scope, StringComparer.Ordinal)
                    )
                    .ToImmutableArray();
                TwitchEventSourceReadinessState state =
                    source.BroadcasterScopes.IsEmpty ? new TwitchEventSourceReadinessState.Ready()
                    : !broadcasterConnected
                        ? new TwitchEventSourceReadinessState.BroadcasterNotConnected()
                    : missingForSource.IsEmpty ? new TwitchEventSourceReadinessState.Ready()
                    : new TwitchEventSourceReadinessState.MissingScopes(missingForSource);
                return new TwitchEventSourceReadiness(
                    source.DefinitionId,
                    descriptor.Display.Name,
                    descriptor.Display.Description,
                    source.SubscriptionTypes,
                    source.BroadcasterScopes,
                    enabledSources.Contains(source.DefinitionId.Value),
                    state
                );
            })
            .ToImmutableArray();
        return new TwitchEventSourceReadinessOutcome.Available(
            sources,
            missingScopes,
            broadcasterConnected
        );
    }
}
