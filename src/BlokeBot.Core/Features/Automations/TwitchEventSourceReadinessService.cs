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

    public sealed record Disabled : TwitchEventSourceReadinessState;

    public sealed record Unavailable(string Reason) : TwitchEventSourceReadinessState;
}

public sealed record TwitchConfiguredSourceReadiness(
    AutomationFlowId FlowId,
    string FlowName,
    AutomationNodeId NodeId,
    string ConfigurationName,
    TwitchEventSourceReadinessState State
);

public sealed record TwitchEventSourceReadiness(
    AutomationDefinitionId DefinitionId,
    string Name,
    string Description,
    string SubscriptionTypes,
    ImmutableArray<string> RequiredBroadcasterScopes,
    bool UsedByEnabledFlow,
    TwitchEventSourceReadinessState State,
    ImmutableArray<TwitchConfiguredSourceReadiness> Configurations
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
        var configuredSources = await _runtime.ConfiguredSourcesAsync(hostId, cancellationToken);
        var tokenStatus = await _broadcasterTokens.GetTokenStatusAsync(
            hostId.Value,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            cancellationToken
        );
        var broadcasterConnected = tokenStatus is TokenStatus.Ready or TokenStatus.MissingScopes;
        var sourceBuilder = ImmutableArray.CreateBuilder<TwitchEventSourceReadiness>();
        foreach (
            var source in TwitchEventAutomationSources.All.Where(source =>
                descriptors.ContainsKey(source.DefinitionId)
                || configuredSources.Any(configured =>
                    configured.DefinitionId == source.DefinitionId
                )
            )
        )
        {
            if (
                !descriptors.TryGetValue(source.DefinitionId, out var descriptor)
                && !_catalog.TryDescribe(source.DefinitionId, out descriptor)
            )
            {
                continue;
            }
            var configurations = configuredSources
                .Where(configured => configured.DefinitionId == source.DefinitionId)
                .ToArray();
            var used = configurations.Any(static configured =>
                configured.Eligibility is AutomationSourceEligibility.Enabled
            );
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
                    "Twitch approval could not be checked. Try again."
                ),
                _ => new TwitchEventSourceReadinessState.BroadcasterNotConnected(),
            };
            var configuredReadiness = configurations
                .Select(configured => new TwitchConfiguredSourceReadiness(
                    configured.FlowId,
                    configured.FlowName,
                    configured.NodeId,
                    ConfigurationName(configured.Configuration, descriptor.Display.Name),
                    ConfiguredState(configured, host, state)
                ))
                .ToImmutableArray();
            sourceBuilder.Add(
                new(
                    source.DefinitionId,
                    descriptor.Display.Name,
                    descriptor.Display.Description,
                    source.SubscriptionTypes,
                    source.BroadcasterScopes,
                    used,
                    state,
                    configuredReadiness
                )
            );
        }
        var sources = sourceBuilder.ToImmutable();
        return new TwitchEventSourceReadinessOutcome.Available(
            sources,
            [
                .. sources
                    .SelectMany(static source => source.Configurations)
                    .Select(static configured => configured.State)
                    .OfType<TwitchEventSourceReadinessState.MissingScopes>()
                    .SelectMany(static missing => missing.Scopes)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ],
            broadcasterConnected
        );
    }

    private TwitchEventSourceReadinessState ConfiguredState(
        AutomationConfiguredSource source,
        BotHost host,
        TwitchEventSourceReadinessState grantState
    ) =>
        source.Eligibility switch
        {
            AutomationSourceEligibility.Disabled => new TwitchEventSourceReadinessState.Disabled(),
            AutomationSourceEligibility.Unavailable unavailable =>
                new TwitchEventSourceReadinessState.Unavailable(unavailable.Reason),
            AutomationSourceEligibility.Enabled => grantState
                is not TwitchEventSourceReadinessState.Ready
                ? grantState
            : !_expanded.Connected(host)
                ? new TwitchEventSourceReadinessState.ObservationUnavailable(
                    "The bot is not connected to this channel. Suppressed or missed events are not replayed."
                )
            : _expanded.ObservationReason(host.Id, source.Configuration!) is { } reason
                ? new TwitchEventSourceReadinessState.ObservationUnavailable(reason)
            : new TwitchEventSourceReadinessState.Ready(),
            _ => throw new InvalidOperationException("Unknown configured source eligibility."),
        };

    private static string ConfigurationName(
        AutomationConfiguration? configuration,
        string sourceName
    ) =>
        configuration switch
        {
            ChatMatchSourceConfiguration { Kind: ChatMatchKind.FirstObserved } =>
                "First observed chatter",
            ChatMatchSourceConfiguration chat => $"{chat.Kind}: {chat.Match}",
            UptimeSourceConfiguration uptime => $"Uptime {uptime.Kind}: {uptime.Duration}",
            AdTimingSourceConfiguration ad => $"Ad {ad.Event}: {ad.Offset}",
            _ => sourceName,
        };
}
