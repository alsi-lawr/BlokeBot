using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Core.Components.Layout;

public partial class HostSelector
{
    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    private IDisposable? _hostedChannelSubscription;
    private IReadOnlyList<BotHostChoice> _visibleHosts = [];
    private int? _selectedHostId;
    private string? _loadedVisibleHostsKey;
    private BotHostSelection? _selection =>
        Session.State.Match<BotHostSelection?>(
            _ => null,
            selected => selected.Selection,
            _ => null
        );

    private string _refreshIconClass =>
        "h-4 w-4 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:2]";

    private string _currentPath => "/" + _navigation.ToBaseRelativePath(_navigation.Uri);

    private string _currentReturnUrl => Uri.EscapeDataString(_currentPath);

    private bool IsAdminEditing() => Session.IsAdminEditing;

    protected override void OnInitialized() =>
        _hostedChannelSubscription = _events.SubscribeForComponentRefresh(
            AppEventKind.HostedChannelsChanged,
            InvokeAsync,
            ReloadForEventAsync,
            StateHasChanged
        );

    protected override async Task OnParametersSetAsync() => await LoadVisibleHostsIfChangedAsync();

    public void Dispose() => _hostedChannelSubscription?.Dispose();

    private async Task LoadVisibleHostsIfChangedAsync()
    {
        var key = VisibleHostsKey();
        if (string.Equals(key, _loadedVisibleHostsKey, StringComparison.Ordinal))
        {
            return;
        }

        await LoadVisibleHostsAsync();
        _loadedVisibleHostsKey = key;
    }

    private async Task ReloadForEventAsync()
    {
        _loadedVisibleHostsKey = null;
        await LoadVisibleHostsAsync();
        _loadedVisibleHostsKey = VisibleHostsKey();
    }

    private async Task LoadVisibleHostsAsync()
    {
        var selectable = Session.AvailableHosts.Where(IsAlternateHost).ToArray();

        _visibleHosts = await _hostedChannels.LoadExistingHostChoicesAsync(
            selectable,
            CancellationToken.None
        );
        _selectedHostId = _visibleHosts.Any(host => host.Id == _selection?.Current.Id)
            ? _selection?.Current.Id
            : null;
    }

    private string VisibleHostsKey()
    {
        var hosts = string.Join(
            "|",
            Session.AvailableHosts.Select(host => $"{host.Id}:{host.Login}:{host.Role}")
        );

        return $"{_selection?.Current.Id}:{Session.Login}:{hosts}";
    }

    private bool IsAlternateHost(BotHostChoice host) =>
        host.Role != AuthRole.Admin
        && host.Role != AuthRole.Streamer
        && !string.Equals(host.Login, Session.Login, StringComparison.OrdinalIgnoreCase);

    private BotHostChoice? SelectedVisibleHost() =>
        _selectedHostId is { } hostId
            ? _visibleHosts.FirstOrDefault(host => host.Id == hostId)
            : null;

    private string SelectHostHref(int hostId) =>
        $"/auth/select-host?hostId={hostId}&returnUrl={_currentReturnUrl}";

    private string _myChannelHref => $"/auth/select-own-host?returnUrl={_currentReturnUrl}";

    private bool ShowMyChannelControl()
    {
        if (_selection is null)
        {
            return false;
        }

        return !IsOwnHost(_selection.Current)
            && (Session.CanCreateHost || Session.AvailableHosts.Any(IsOwnHost));
    }

    private bool IsOwnHost(BotHostChoice host) =>
        host.Role == AuthRole.Streamer
        && string.Equals(host.Login, Session.Login, StringComparison.OrdinalIgnoreCase);

    private string HostItemClass(BotHostChoice host)
    {
        var selected =
            host.Id == _selectedHostId ? "bg-purple-50 text-[#6f2bdc]" : "text-slate-800";

        return $"menu-item grid grid-cols-[1.75rem_minmax(0,1fr)_1.25rem] items-center gap-2 px-2.5 py-2 text-sm font-semibold {selected}";
    }

    private static string ChannelInitial(BotHostChoice host)
    {
        var text = !string.IsNullOrWhiteSpace(host.DisplayName) ? host.DisplayName : host.Login;

        return string.IsNullOrWhiteSpace(text) ? "?" : text[..1].ToUpperInvariant();
    }

    private RenderFragment ChannelImage(BotHostChoice host) =>
        builder =>
        {
            if (!string.IsNullOrWhiteSpace(host.ProfileImageUrl))
            {
                builder.OpenElement(0, "img");
                builder.AddAttribute(
                    1,
                    "class",
                    "h-7 w-7 shrink-0 rounded-full border border-slate-200 object-cover"
                );
                builder.AddAttribute(2, "src", host.ProfileImageUrl);
                builder.AddAttribute(3, "alt", string.Empty);
                builder.CloseElement();
                return;
            }

            builder.OpenElement(4, "span");
            builder.AddAttribute(
                5,
                "class",
                "grid h-7 w-7 shrink-0 place-items-center rounded-full bg-slate-100 text-xs font-bold text-slate-500 ring-1 ring-slate-200"
            );
            builder.AddContent(6, ChannelInitial(host));
            builder.CloseElement();
        };
}
