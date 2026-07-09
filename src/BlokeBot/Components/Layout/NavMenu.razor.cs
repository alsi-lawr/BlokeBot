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
using BlokeBot.Features.HostedChannels;
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

public partial class NavMenu
{
    private const string GuessingOpenStorageKey = "blokebot.sidebar.guessing.open";
    private const string PointsOpenStorageKey = "blokebot.sidebar.points.open";

    private bool guessingOpen = true;
    private bool pointsOpen = true;
    private IDisposable? hostedChannelSubscription;
    private IReadOnlyDictionary<int, HostFeatureFlags> hostedFeatures =
        new Dictionary<int, HostFeatureFlags>();
    private IReadOnlySet<int> existingHostIds = new HashSet<int>();
    private IJSObjectReference? module;

    protected override async Task OnInitializedAsync()
    {
        hostedChannelSubscription = Events.SubscribeForComponentRefresh(
            AppEventKind.HostedChannelsChanged,
            work => InvokeAsync(work),
            LoadHostedFeaturesAsync,
            StateHasChanged
        );
        await LoadHostedFeaturesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            module = await Js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Components/Layout/NavMenu.razor.js"
            );
            guessingOpen = await module.InvokeAsync<bool>(
                "readBoolean",
                GuessingOpenStorageKey,
                true
            );
            pointsOpen = await module.InvokeAsync<bool>("readBoolean", PointsOpenStorageKey, true);
            StateHasChanged();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        hostedChannelSubscription?.Dispose();

        if (module is null)
            return;

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private async Task ToggleGuessingAsync()
    {
        guessingOpen = !guessingOpen;
        if (module is not null)
            await module.InvokeVoidAsync("writeBoolean", GuessingOpenStorageKey, guessingOpen);
    }

    private async Task TogglePointsAsync()
    {
        pointsOpen = !pointsOpen;
        if (module is not null)
            await module.InvokeVoidAsync("writeBoolean", PointsOpenStorageKey, pointsOpen);
    }

    private bool FeatureIsVisible(
        AuthenticatedSession session,
        BotHostSelection? selection,
        HostFeatureFlags feature
    )
    {
        if (!session.CanUseBotFunctions(existingHostIds) || selection is null)
            return false;

        return hostedFeatures.TryGetValue(selection.Current.Id, out var features)
            && features.Contains(feature);
    }

    private async Task LoadHostedFeaturesAsync()
    {
        hostedFeatures = await Features.LoadHostedFeaturesAsync(CancellationToken.None);
        existingHostIds = hostedFeatures.Keys.ToHashSet();
    }
}
