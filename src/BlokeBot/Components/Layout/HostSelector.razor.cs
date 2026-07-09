using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Components.Layout;

public partial class HostSelector
{
    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    private IDisposable? hostedChannelSubscription;
    private IReadOnlyList<BotHostChoice> visibleHosts = [];
    private int? selectedHostId;
    private string? loadedVisibleHostsKey;
    private BotHostSelection? Selection => Session.HostSelection;

    private string RefreshIconClass =>
        "h-4 w-4 fill-none stroke-current [stroke-linecap:round] [stroke-linejoin:round] [stroke-width:2]";

    private string CurrentPath => "/" + Navigation.ToBaseRelativePath(Navigation.Uri);

    private string CurrentReturnUrl
    {
        get { return Uri.EscapeDataString(CurrentPath); }
    }

    private bool IsAdminEditing() => Session.IsAdminEditing;

    protected override void OnInitialized()
    {
        hostedChannelSubscription = Events.SubscribeForComponentRefresh(
            AppEventKind.HostedChannelsChanged,
            work => InvokeAsync(work),
            ReloadForEventAsync,
            StateHasChanged
        );
    }

    protected override async Task OnParametersSetAsync()
    {
        await LoadVisibleHostsIfChangedAsync();
    }

    public void Dispose()
    {
        hostedChannelSubscription?.Dispose();
    }

    private async Task LoadVisibleHostsIfChangedAsync()
    {
        var key = VisibleHostsKey();
        if (string.Equals(key, loadedVisibleHostsKey, StringComparison.Ordinal))
            return;

        await LoadVisibleHostsAsync();
        loadedVisibleHostsKey = key;
    }

    private async Task ReloadForEventAsync()
    {
        loadedVisibleHostsKey = null;
        await LoadVisibleHostsAsync();
        loadedVisibleHostsKey = VisibleHostsKey();
    }

    private async Task LoadVisibleHostsAsync()
    {
        var selectable = Session.AvailableHosts.Where(IsAlternateHost).ToArray();

        visibleHosts = await HostedChannels.LoadExistingHostChoicesAsync(
            selectable,
            CancellationToken.None
        );
        selectedHostId = visibleHosts.Any(host => host.Id == Selection?.Current.Id)
            ? Selection?.Current.Id
            : null;
    }

    private string VisibleHostsKey()
    {
        var hosts = string.Join(
            "|",
            Session.AvailableHosts.Select(host => $"{host.Id}:{host.Login}:{host.Role}")
        );

        return $"{Selection?.Current.Id}:{Session.Login}:{hosts}";
    }

    private bool IsAlternateHost(BotHostChoice host) =>
        host.Role != AuthRole.Admin
        && host.Role != AuthRole.Streamer
        && !string.Equals(host.Login, Session.Login, StringComparison.OrdinalIgnoreCase);

    private BotHostChoice? SelectedVisibleHost() =>
        selectedHostId is { } hostId
            ? visibleHosts.FirstOrDefault(host => host.Id == hostId)
            : null;

    private string SelectHostHref(int hostId) =>
        $"/auth/select-host?hostId={hostId}&returnUrl={CurrentReturnUrl}";

    private string MyChannelHref => $"/auth/select-own-host?returnUrl={CurrentReturnUrl}";

    private static string SelectorShellClass(bool showMyChannel) =>
        showMyChannel ? "host-selector host-selector--with-my-channel" : "host-selector";

    private bool ShowMyChannelControl()
    {
        if (Selection is null)
            return false;

        return !IsOwnHost(Selection.Current)
            && (Session.CanCreateHost || Session.AvailableHosts.Any(IsOwnHost));
    }

    private bool IsOwnHost(BotHostChoice host) =>
        host.Role == AuthRole.Streamer
        && string.Equals(host.Login, Session.Login, StringComparison.OrdinalIgnoreCase);

    private string HostItemClass(BotHostChoice host)
    {
        var selected = host.Id == selectedHostId ? "bg-purple-50 text-[#6f2bdc]" : "text-slate-800";

        return $"menu-item grid grid-cols-[1.75rem_minmax(0,1fr)_1.25rem] items-center gap-2 px-2.5 py-2 text-sm font-semibold {selected}";
    }

    private static string ChannelInitial(BotHostChoice host)
    {
        var text = !string.IsNullOrWhiteSpace(host.DisplayName) ? host.DisplayName : host.Login;

        return string.IsNullOrWhiteSpace(text) ? "?" : text[..1].ToUpperInvariant();
    }

    private RenderFragment ChannelImage(BotHostChoice host)
    {
        return builder =>
        {
            if (!string.IsNullOrWhiteSpace(host.ProfileImageUrl))
            {
                builder.OpenElement(0, "img");
                builder.AddAttribute(
                    1,
                    "class",
                    "h-7 w-7 rounded-full border border-slate-200 object-cover"
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
                "grid h-7 w-7 place-items-center rounded-full bg-slate-100 text-xs font-bold text-slate-500 ring-1 ring-slate-200"
            );
            builder.AddContent(6, ChannelInitial(host));
            builder.CloseElement();
        };
    }
}
