using System.Diagnostics;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Hosts;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class SelectedChannelBotStatus
{
    private int? _loadedStatusHostId;
    private IDisposable? _hostedChannelSubscription;
    private HostedChannelRuntimeSummary? _selectedHostStatus;

    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    private BotHostSelection? _selection =>
        Session.State.Match<BotHostSelection?>(
            _ => null,
            selected => selected.Selection,
            _ => null
        );

    private bool _selectedHostBotAuthorized =>
        _selectedHostStatus?.IsChannelBotAuthorized == true
        && _selectedHostStatus.ChannelBotAuthorizationScopesCurrent;

    private string _selectedBotStatusShellClass
    {
        get
        {
            var color = _selectedHostStatus switch
            {
                { IsChannelBotAuthorized: false } => "border-amber-200 bg-amber-50 text-amber-700",
                { ChannelBotAuthorizationScopesCurrent: false } =>
                    "border-amber-200 bg-amber-50 text-amber-700",
                { } status => status.Lifecycle.Match(
                    static _ => "app-blue-status",
                    static _ => "border-orange-200 bg-orange-50 text-orange-700",
                    static _ => "border-emerald-200 bg-emerald-50 text-emerald-700",
                    static _ => "border-purple-200 bg-purple-50 text-purple-700"
                ),
                null => "border-slate-200 bg-slate-100 text-slate-600",
            };

            return $"selected-channel-bot-status status-pill border shadow-sm {color}";
        }
    }

    private string _selectedBotStatusDotClass
    {
        get
        {
            var color = _selectedHostStatus switch
            {
                { IsChannelBotAuthorized: false } => "bg-amber-500",
                { ChannelBotAuthorizationScopesCurrent: false } => "bg-amber-500",
                { } status => status.Lifecycle.Match(
                    static _ => "app-blue-dot",
                    static _ => "bg-orange-500",
                    static _ => "bg-emerald-500",
                    static _ => "bg-purple-500"
                ),
                null => "bg-slate-400",
            };

            return $"status-pill__dot {color}";
        }
    }

    private string _selectedBotStatusText =>
        _selectedHostStatus switch
        {
            null => "unknown",
            { } status => status.Lifecycle.Match(
                _ =>
                    status.IsChannelBotAuthorized switch
                    {
                        true => status.ChannelBotAuthorizationScopesCurrent switch
                        {
                            true => "chat connected",
                            false when CanAuthorizeSelectedHost() => "reconnect bot",
                            _ => "Channel owner needs to reconnect the bot",
                        },
                        false when CanAuthorizeSelectedHost() => "connect bot",
                        _ => "Channel owner needs to reconnect the bot",
                    },
                static _ => "bot starting",
                static _ => "bot running",
                static _ => "bot stopping"
            ),
        };

    protected override void OnInitialized() =>
        _hostedChannelSubscription = _events.SubscribeForComponentRefresh(
            AppEventKind.HostedChannelsChanged,
            InvokeAsync,
            ReloadForEventAsync,
            StateHasChanged
        );

    protected override async Task OnParametersSetAsync() => await LoadSelectedHostStatusAsync();

    public void Dispose() => _hostedChannelSubscription?.Dispose();

    private bool CanAuthorizeSelectedHost() => Session.CanAuthorizeSelectedHost;

    private async Task LoadSelectedHostStatusAsync()
    {
        if (_selection is null)
        {
            _loadedStatusHostId = null;
            _selectedHostStatus = null;
            return;
        }

        if (_loadedStatusHostId == _selection.Current.Id)
        {
            return;
        }

        _loadedStatusHostId = _selection.Current.Id;
        var result = await _hostedChannels
            .LoadHostRuntimeSummary(_selection.Current.Id)
            .ExecuteAsync(CancellationToken.None);
        _selectedHostStatus = result.Match(
            option => option.Match<HostedChannelRuntimeSummary?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
    }

    private async Task ReloadForEventAsync()
    {
        _loadedStatusHostId = null;
        await LoadSelectedHostStatusAsync();
    }
}
