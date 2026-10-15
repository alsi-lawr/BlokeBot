using System.Collections.Immutable;
using BlokeBot.Core.Components;
using BlokeBot.Eventing;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEventsPage
{
    private TwitchEventSourceReadinessOutcome? _outcome;
    private bool _loading = true;
    private bool _loadFailed;
    private bool _disposed;
    private long _loadGeneration;
    private int _subscribedHost;
    private IDisposable? _readinessSubscription;

    [Inject]
    private AutomationUiChanges _sourceChanges { get; set; } = default!;

    [Inject]
    private IBotRuntimeStatusAccessor _botStatus { get; set; } = default!;

    [Inject]
    private IEventSubChannelStatusAccessor _channelStatus { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        _botStatus.Changed += ConnectionChanged;
        _channelStatus.Changed += ConnectionChanged;
        await LoadAsync();
    }

    private void ConnectionChanged() => _ = RefreshConnectionAsync();

    private async Task RefreshConnectionAsync()
    {
        try
        {
            if (!_disposed)
            {
                await InvokeAsync(async () =>
                {
                    if (_disposed)
                    {
                        return;
                    }
                    await LoadAsync();
                    if (!_disposed)
                    {
                        StateHasChanged();
                    }
                });
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                ReportUiFault(nameof(RefreshConnectionAsync), exception);
            }
        }
    }

    private void SubscribeToHost(int hostId)
    {
        if (_subscribedHost == hostId)
        {
            return;
        }
        _readinessSubscription?.Dispose();
        _subscribedHost = hostId;
        _readinessSubscription =
            hostId == 0
                ? null
                : _sourceChanges.Subscribe(
                    new AutomationUiChange.SourceReadiness(new(hostId)),
                    ObserverIdentity.For(typeof(AutomationEventsPage)),
                    async (_, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        await InvokeAsync(async () =>
                        {
                            if (_disposed || _subscribedHost != hostId)
                            {
                                return;
                            }
                            await LoadAsync();
                            if (!_disposed && _subscribedHost == hostId)
                            {
                                StateHasChanged();
                            }
                        });
                    }
                );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _loadGeneration++;
            _botStatus.Changed -= ConnectionChanged;
            _channelStatus.Changed -= ConnectionChanged;
            _readinessSubscription?.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task LoadAsync()
    {
        if (_disposed)
        {
            return;
        }
        var generation = ++_loadGeneration;
        _loading = true;
        _loadFailed = false;
        try
        {
            _ = await AuthenticationState;
            if (_disposed || generation != _loadGeneration)
            {
                return;
            }
            _ = await LoadPageContextAsync();
            if (_disposed || generation != _loadGeneration)
            {
                return;
            }
            var hostId = HostId;
            SubscribeToHost(hostId);
            var outcome =
                hostId == 0
                    ? null
                    : await _readiness.LoadAsync(new(hostId), CancellationToken.None);
            if (!_disposed && generation == _loadGeneration && _subscribedHost == hostId)
            {
                _outcome = outcome;
            }
        }
        catch (Exception exception)
        {
            if (_disposed || generation != _loadGeneration)
            {
                return;
            }
            _outcome = null;
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            if (!_disposed && generation == _loadGeneration)
            {
                _loading = false;
            }
        }
    }

    private static bool RequiresReconnect(TwitchEventSourceReadinessOutcome.Available available) =>
        available
            .Sources.SelectMany(static source => source.Configurations)
            .Any(static source =>
                source.State
                    is TwitchEventSourceReadinessState.BroadcasterNotConnected
                        or TwitchEventSourceReadinessState.MissingScopes
            );

    private static string ReconnectDescription(
        TwitchEventSourceReadinessOutcome.Available available
    ) =>
        !available.BroadcasterConnected
            ? "An enabled flow needs this channel's Twitch approval. Connect the channel to activate that source."
            : $"Your enabled flows need {FormatScopes(available.MissingBroadcasterScopes)}. Reconnect to approve these missing permissions.";

    private static string StateToken(TwitchEventSourceReadinessState state) =>
        state switch
        {
            TwitchEventSourceReadinessState.Ready => "ready",
            TwitchEventSourceReadinessState.MissingScopes => "missing-scopes",
            TwitchEventSourceReadinessState.ObservationUnavailable => "observation-unavailable",
            TwitchEventSourceReadinessState.Disabled => "disabled",
            TwitchEventSourceReadinessState.Unavailable => "unavailable",
            _ => "not-connected",
        };

    private static string StateLabel(TwitchEventSourceReadinessState state) =>
        state switch
        {
            TwitchEventSourceReadinessState.Ready => "Ready",
            TwitchEventSourceReadinessState.MissingScopes => "Reconnect needed",
            TwitchEventSourceReadinessState.ObservationUnavailable => "Observation unavailable",
            TwitchEventSourceReadinessState.Disabled => "Flow disabled",
            TwitchEventSourceReadinessState.Unavailable => "Unavailable",
            _ => "Twitch connection needed",
        };

    private static string GrantLabel(TwitchEventSourceReadinessState state) =>
        state switch
        {
            TwitchEventSourceReadinessState.Ready => "Approval ready",
            TwitchEventSourceReadinessState.MissingScopes => "Additional approval needed",
            TwitchEventSourceReadinessState.ObservationUnavailable => "Approval unavailable",
            _ => "No saved approval",
        };

    private static string? StateReason(TwitchEventSourceReadinessState state) =>
        state switch
        {
            TwitchEventSourceReadinessState.Disabled =>
                "Enable this saved flow to activate its source.",
            TwitchEventSourceReadinessState.Unavailable unavailable => unavailable.Reason,
            TwitchEventSourceReadinessState.ObservationUnavailable unavailable =>
                unavailable.Reason,
            TwitchEventSourceReadinessState.MissingScopes missing =>
                $"Reconnect to Twitch to approve {FormatScopes(missing.Scopes)}.",
            TwitchEventSourceReadinessState.BroadcasterNotConnected =>
                "Connect this channel's Twitch integration to activate this source.",
            _ => null,
        };

    private static string BadgeClass(TwitchEventSourceReadinessState state) =>
        state is TwitchEventSourceReadinessState.Ready
            ? "inline-flex shrink-0 items-center rounded-full bg-emerald-100 px-3 py-1 text-xs font-bold text-emerald-800"
            : "inline-flex shrink-0 items-center rounded-full bg-amber-100 px-3 py-1 text-xs font-bold text-amber-800";

    private static string ScopesLabel(TwitchEventSourceReadiness source) =>
        source.RequiredBroadcasterScopes.IsEmpty
            ? "None beyond the bot connection."
            : FormatScopes(source.RequiredBroadcasterScopes);

    private static string FormatScopes(ImmutableArray<string> scopes) => string.Join(", ", scopes);
}
