using System.Collections.Concurrent;
using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class ExpandedAutomationRuntime(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationRuntimeService runtime,
    AutomationCatalogService catalog,
    AutomationCountdownService countdowns,
    TimeProvider clock,
    IHostStreamLivenessProvider streams,
    IBotAccountProvider accounts,
    IHostBroadcasterTokenStatusProvider broadcasterTokens,
    HelixClient helix,
    BotSettings settings,
    IBotRuntimeStatusAccessor connection,
    ILogger<ExpandedAutomationRuntime> logger,
    IEventSubChannelStatusAccessor? channels = null
) : IExpandedTwitchEventObserver, IEventSubExactRequirementSource, IHostFeatureActivationObserver
{
    private readonly ConcurrentDictionary<(int Host, string Key), DateTimeOffset> _nextSchedules =
        new();
    private readonly ConcurrentDictionary<int, Guid> _connections = new();
    private readonly ConcurrentDictionary<int, BotHost> _observedHosts = new();
    private readonly Dictionary<int, GoalSelection> _goalSelections = new();

    internal void ObserveConnectionChanges(bool enabled)
    {
        if (enabled)
        {
            connection.Changed += ConnectionChanged;
            channels?.Changed += ConnectionChanged;
        }
        else
        {
            connection.Changed -= ConnectionChanged;
            channels?.Changed -= ConnectionChanged;
        }
    }

    private void ConnectionChanged()
    {
        foreach (var host in _observedHosts.Values.Where(h => !Connected(h)))
        {
            foreach (var key in _nextSchedules.Keys.Where(k => k.Host == host.Id))
            {
                _ = _nextSchedules.TryRemove(key, out _);
            }
            _connections[host.Id] = Guid.NewGuid();
            _ = _ads.TryRemove(host.Id, out _);
            _ = _metadata.TryRemove(host.Id, out _);
        }
    }

    private Guid GoalEpoch(int hostId, IEnumerable<GoalSourceConfiguration> sources)
    {
        var fingerprint = string.Join(
            "|",
            sources.Select(s => s.ToString()).Order(StringComparer.Ordinal)
        );
        var connected = ConnectionId(hostId);
        lock (_observationGate)
        {
            if (
                !_goalSelections.TryGetValue(hostId, out var prior)
                || prior.Connection != connected
                || prior.Fingerprint != fingerprint
            )
            {
                _goalSelections[hostId] = prior = new(connected, fingerprint, Guid.NewGuid());
            }
            return prior.Epoch;
        }
    }

    private sealed record GoalSelection(Guid Connection, string Fingerprint, Guid Epoch);

    private readonly ConcurrentDictionary<int, bool> _wasConnected = new();
    private readonly ConcurrentDictionary<int, AdObservation> _ads = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _polls = new();
    private readonly ConcurrentDictionary<int, string> _readiness = new();
    private readonly ConcurrentDictionary<int, string> _uptimeReadiness = new();
    private readonly ConcurrentDictionary<int, MetadataObservation> _metadata = new();
    private readonly Lock _observationGate = new();

    internal string? UnavailableReason(int hostId) => _readiness.GetValueOrDefault(hostId);

    internal string? ObservationReason(int hostId, AutomationDefinitionId source) =>
        source == AutomationDefinitionIds.AdTimingSource
            ? !_polls.ContainsKey(hostId)
                ? "Waiting for the current Twitch ad schedule. No derived deadline is assumed."
                : UnavailableReason(hostId)
            : source == AutomationDefinitionIds.UptimeSource
            || source == AutomationDefinitionIds.ChatMatchSource
                ? _uptimeReadiness.GetValueOrDefault(hostId)
                : null;

    internal bool Connected(BotHost host) =>
        connection.Current is BotRuntimeStatus.Connected active
        && active.Channels.Contains(host.Login, StringComparer.OrdinalIgnoreCase)
        && (
            channels is null
            || channels.Current.Channels.Any(c =>
                c.Channel == host.Login && c is EventSubChannelStatus.Healthy
            )
        );

    private Guid ConnectionId(int hostId) => _connections.GetOrAdd(hostId, _ => Guid.NewGuid());

    private async Task<BotHost?> HostAsync(
        string broadcasterId,
        HostFeatureFlags required,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            return null;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(h => h.TwitchUserId == broadcasterId, ct);
        return
            host is not null
            && host.EnabledFeatures.Contains(required | HostFeatureFlags.Automations)
            ? host
            : null;
    }

    private async Task<bool> AcceptEventAsync(int hostId, DateTimeOffset at, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return !await db.AutomationSourceAdmissions.AnyAsync(
            s => s.HostId == hostId && s.AcceptEventsAfterUtc >= at.UtcDateTime,
            ct
        );
    }

    internal async Task<ImmutableArray<AutomationConfiguration>> SourcesAsync(
        BotHost host,
        CancellationToken ct
    )
    {
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nodes = await db
            .AutomationFlowNodes.AsNoTracking()
            .Where(n => n.Flow.HostId == host.Id && n.Flow.IsEnabled)
            .ToArrayAsync(ct);
        var sources = ImmutableArray.CreateBuilder<AutomationConfiguration>();
        foreach (var n in nodes)
        {
            System.Text.Json.JsonDocument json;
            try
            {
                json = System.Text.Json.JsonDocument.Parse(n.ConfigurationJson);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            using var configured = json;
            if (
                catalog.ValidatePersistedDefinition(
                    new(n.DefinitionId, n.DefinitionSchemaVersion, json.RootElement.Clone())
                )
                    is AutomationConfigurationCheck.Valid
                    {
                        Definition.Kind: AutomationNodeKind.Source
                    } valid
                && host.EnabledFeatures.Contains(
                    AutomationRequiredFeatures.ForDefinitions([n.DefinitionId])
                )
            )
            {
                sources.Add(valid.Configuration);
            }
        }
        return sources.Distinct().ToImmutableArray();
    }

    public async ValueTask<IReadOnlyList<EventSubExactSubscription>> GetRequirementsAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(h => h.Login == Login.Normalize(channel), cancellationToken);
        if (host is null)
        {
            return [];
        }
        var sources = await SourcesAsync(host, cancellationToken);
        _ = GoalEpoch(host.Id, sources.OfType<GoalSourceConfiguration>());
        if (!sources.OfType<AdTimingSourceConfiguration>().Any())
        {
            _ = _ads.TryRemove(host.Id, out _);
            _ = _polls.TryRemove(host.Id, out _);
        }
        var result = new List<EventSubExactSubscription>();
        if (
            sources.OfType<AdTimingSourceConfiguration>().Any()
            && await AuthorizedAsync(host, ["channel:read:ads"], cancellationToken)
        )
        {
            result.Add(new("channel.ad_break.begin", "1"));
        }
        if (
            sources.OfType<GoalSourceConfiguration>().Any()
            && await AuthorizedAsync(host, ["channel:read:goals"], cancellationToken)
        )
        {
            result.AddRange([
                new("channel.goal.begin", "1"),
                new("channel.goal.progress", "1"),
                new("channel.goal.end", "1"),
            ]);
        }
        if (
            sources.OfType<ModerationSourceConfiguration>().Any()
            && await AuthorizedAsync(host, EventSubModerationActions.ReadScopes, cancellationToken)
        )
        {
            result.Add(new("channel.moderate", "2"));
        }
        if (sources.OfType<ChatSettingsSourceConfiguration>().Any())
        {
            result.Add(new("channel.chat_settings.update", "1"));
        }
        return result;
    }

    private async Task<bool> AuthorizedAsync(
        BotHost host,
        IEnumerable<string> scopes,
        CancellationToken ct
    ) =>
        (await broadcasterTokens.GetTokenStatusAsync(host.Id, scopes, ct)).Match(
            _ => false,
            _ => false,
            _ => false,
            missing =>
                missing.RequiredScopes.All(scope =>
                    EventSubModerationActions.ScopeSatisfied(missing.GrantedScopes, scope)
                ),
            _ => true
        );

    public ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (change.Feature == HostFeatureFlags.Automations)
        {
            foreach (var key in _nextSchedules.Keys.Where(k => k.Host == change.HostId))
            {
                _ = _nextSchedules.TryRemove(key, out _);
            }
            _ = _ads.TryRemove(change.HostId, out _);
            _ = _metadata.TryRemove(change.HostId, out _);
            _connections[change.HostId] = Guid.NewGuid();
        }
        return ValueTask.FromResult<HostFeatureAutomaticWorkResult>(
            new HostFeatureAutomaticWorkResult.Complete()
        );
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        await countdowns.InitializeAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db.Hosts.AsNoTracking().ToArrayAsync(ct);
        foreach (var host in hosts)
        {
            _observedHosts[host.Id] = host;
            var connected = Connected(host);
            if (_wasConnected.TryGetValue(host.Id, out var old) && old && !connected)
            {
                _connections[host.Id] = Guid.NewGuid();
                _ = _metadata.TryRemove(host.Id, out _);
                _ = _ads.TryRemove(host.Id, out _);
            }
            _wasConnected[host.Id] = connected;
            var sources = await SourcesAsync(host, ct);
            _ = GoalEpoch(host.Id, sources.OfType<GoalSourceConfiguration>());
            if (!sources.OfType<AdTimingSourceConfiguration>().Any())
            {
                _ = _ads.TryRemove(host.Id, out _);
                _ = _polls.TryRemove(host.Id, out _);
            }
            await countdowns.TickAsync(
                host,
                sources.OfType<CountdownSourceConfiguration>().ToArray(),
                ct
            );
            var now = clock.GetUtcNow();
            foreach (var schedule in sources.OfType<ScheduledTimeSourceConfiguration>())
            {
                var key = (host.Id, schedule.ToString());
                if (!_nextSchedules.TryGetValue(key, out var next))
                {
                    if (AutomationScheduleTime.Next(schedule, now) is { } first)
                    {
                        _nextSchedules[key] = first;
                    }
                    continue;
                }
                if (next > now)
                {
                    continue;
                }
                if (AutomationScheduleTime.Next(schedule, now) is { } future)
                {
                    _nextSchedules[key] = future;
                }
                else
                {
                    _nextSchedules[key] = DateTimeOffset.MaxValue;
                }
                if (!connected || now - next > TimeSpan.FromSeconds(2))
                {
                    continue;
                }
                if (
                    schedule.LiveOnly
                    && await LiveAsync(host, ct) is not HostStreamLivenessOutcome.Live
                )
                {
                    continue;
                }
                _ = await runtime.DispatchExpandedAsync(
                    Create(
                        host,
                        AutomationDefinitionIds.ScheduledTimeSource,
                        $"{schedule}:{next.UtcTicks}",
                        next,
                        now,
                        [Time("scheduled-at", next), Text("schedule-zone", schedule.Zone)]
                    ),
                    c => c == schedule,
                    ct
                );
            }
            foreach (
                var key in _nextSchedules.Keys.Where(k =>
                    k.Host == host.Id
                    && !sources
                        .OfType<ScheduledTimeSourceConfiguration>()
                        .Any(s => s.ToString() == k.Key)
                )
            )
            {
                _ = _nextSchedules.TryRemove(key, out _);
            }
            if (!connected || !host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
            {
                continue;
            }
            if (
                sources.OfType<UptimeSourceConfiguration>().Any()
                || sources
                    .OfType<ChatMatchSourceConfiguration>()
                    .Any(s => s.Kind == ChatMatchKind.FirstObserved)
            )
            {
                await UptimeAsync(host, sources, ct);
            }
            if (
                sources.OfType<AdTimingSourceConfiguration>().Any()
                && (
                    !_polls.TryGetValue(host.Id, out var last)
                    || now - last >= TimeSpan.FromSeconds(5)
                )
            )
            {
                _polls[host.Id] = now;
                await PollAdsAsync(
                    host,
                    sources.OfType<AdTimingSourceConfiguration>().ToArray(),
                    ct
                );
            }
        }
    }

    private async Task<HostStreamLivenessOutcome> LiveAsync(BotHost host, CancellationToken ct) =>
        (await streams.GetStreamLiveness(host.Login).ExecuteAsync(ct)).Match(
            v => v,
            _ => throw new InvalidOperationException("Liveness has no error branch.")
        );

    private sealed record MetadataObservation(
        string Title,
        string CategoryId,
        DateTimeOffset At,
        Guid Connection
    );

    private sealed record AdObservation(
        DateTimeOffset StartedAt,
        int Duration,
        DateTimeOffset? NextAdAt,
        DateTimeOffset ObservedAt
    );
}

internal sealed class ExpandedAutomationWorker(
    ExpandedAutomationRuntime runtime,
    TimeProvider clock,
    ILogger<ExpandedAutomationWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        runtime.ObserveConnectionChanges(true);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await runtime.TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    logger.LogError(
                        "Automation source observation failed ({FailureType}); no successful trigger is inferred.",
                        error.GetType().Name
                    );
                }
            }
        }
        finally
        {
            runtime.ObserveConnectionChanges(false);
        }
    }
}
