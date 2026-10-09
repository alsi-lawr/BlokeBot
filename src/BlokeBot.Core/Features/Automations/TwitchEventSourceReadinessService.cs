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

    public sealed record ObservationUnavailable(string Reason) : TwitchEventSourceReadinessState;

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
/// Reports source-specific observation grants and live connection availability. Existing milestone
/// grants remain unchanged; additional read-only grants are requested only for enabled sources.
/// </summary>
public sealed class TwitchEventSourceReadinessService
{
    private readonly IDbContextFactory<BlokeBotDbContext> _dbFactory;
    private readonly AutomationCatalogService _catalog;
    private readonly AutomationRuntimeService _runtime;
    private readonly IHostBroadcasterTokenStatusProvider _broadcasterTokens;
    private readonly ExpandedAutomationRuntime _expanded;

    internal TwitchEventSourceReadinessService(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        AutomationCatalogService catalog,
        AutomationRuntimeService runtime,
        IHostBroadcasterTokenStatusProvider broadcasterTokens,
        ExpandedAutomationRuntime expanded
    )
    {
        _dbFactory = dbFactory;
        _catalog = catalog;
        _runtime = runtime;
        _broadcasterTokens = broadcasterTokens;
        _expanded = expanded;
    }

    public async Task<ImmutableArray<string>> AuthorizationScopesAsync(
        int hostId,
        CancellationToken cancellation
    )
    {
        var enabled = await _runtime.EnabledSourceDefinitionIdsAsync(new(hostId), cancellation);
        return
        [
            .. HostBroadcasterAuthorizationService
                .MilestoneScopes.Concat(
                    TwitchEventAutomationSources
                        .All.Where(s => enabled.Contains(s.DefinitionId.Value))
                        .SelectMany(s => s.BroadcasterScopes)
                )
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public async Task<TwitchEventSourceReadinessOutcome> LoadAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
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

        var snapshot = await _catalog.DiscoverAsync(hostId, cancellationToken);
        var descriptors = snapshot.Definitions.ToImmutableDictionary(static value => value.Id);
        var enabledSources = await _runtime.EnabledSourceDefinitionIdsAsync(
            hostId,
            cancellationToken
        );
        var tokenStatus = await _broadcasterTokens.GetTokenStatusAsync(
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
        var sourceBuilder = ImmutableArray.CreateBuilder<TwitchEventSourceReadiness>();
        foreach (
            var source in TwitchEventAutomationSources.All.Where(source =>
                descriptors.ContainsKey(source.DefinitionId)
            )
        )
        {
            var descriptor = descriptors[source.DefinitionId];
            var used = enabledSources.Contains(source.DefinitionId.Value);
            var sourceStatus = source.BroadcasterScopes.IsEmpty
                ? null
                : await _broadcasterTokens.GetTokenStatusAsync(
                    host.Id,
                    source.BroadcasterScopes,
                    cancellationToken
                );
            TwitchEventSourceReadinessState state = sourceStatus switch
            {
                TokenStatus.MissingScopes missing
                    when source
                        .BroadcasterScopes.Where(scope =>
                            missing.Missing.Contains(scope, StringComparer.Ordinal)
                        )
                        .All(scope =>
                            EventSubModerationActions.ScopeSatisfied(missing.GrantedScopes, scope)
                        ) => new TwitchEventSourceReadinessState.Ready(),
                TokenStatus.MissingScopes missing =>
                    new TwitchEventSourceReadinessState.MissingScopes([
                        .. source.BroadcasterScopes.Where(scope =>
                            missing.Missing.Contains(scope, StringComparer.Ordinal)
                            && !EventSubModerationActions.ScopeSatisfied(
                                missing.GrantedScopes,
                                scope
                            )
                        ),
                    ]),
                TokenStatus.Ready or null => new TwitchEventSourceReadinessState.Ready(),
                TokenStatus.Unknown => new TwitchEventSourceReadinessState.ObservationUnavailable(
                    "Twitch authorization could not be checked. No observation authority is assumed."
                ),
                _ => new TwitchEventSourceReadinessState.BroadcasterNotConnected(),
            };
            if (used && state is TwitchEventSourceReadinessState.Ready)
            {
                if (!_expanded.Connected(host))
                {
                    state = new TwitchEventSourceReadinessState.ObservationUnavailable(
                        "The bot is not connected to this channel. Suppressed or missed events are not replayed."
                    );
                }
                else if (_expanded.ObservationReason(host.Id, source.DefinitionId) is { } reason)
                {
                    state = new TwitchEventSourceReadinessState.ObservationUnavailable(reason);
                }
            }
            sourceBuilder.Add(
                new(
                    source.DefinitionId,
                    descriptor.Display.Name,
                    descriptor.Display.Description,
                    source.SubscriptionTypes,
                    source.BroadcasterScopes,
                    used,
                    state
                )
            );
        }
        var sources = sourceBuilder.ToImmutable();
        return new TwitchEventSourceReadinessOutcome.Available(
            sources,
            missingScopes,
            broadcasterConnected
        );
    }
}
