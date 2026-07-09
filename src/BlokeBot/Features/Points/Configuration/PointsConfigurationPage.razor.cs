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
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Points.Configuration;

public partial class PointsConfigurationPage
{
    private const string WhisperDisabledTooltip =
        "Enable whisper responses in Channel setup before using whisper replies.";

    private PointsConfiguration? config;
    private bool featureEnabled;

    private bool WhisperDisabled => config?.WhisperResponsesEnabled != true;

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
            Events.SubscribeForComponentRefresh(
                AppEventKind.HostedChannelsChanged,
                work => InvokeAsync(work),
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await LoadPageContextAsync();
        featureEnabled =
            HostId != 0
            && await Features.IsEnabledAsync(
                HostId,
                HostFeatureFlags.Points,
                CancellationToken.None
            );
        config = featureEnabled
            ? await Configuration.LoadConfigurationAsync(HostId, CancellationToken.None)
            : null;
    }

    private async Task SaveAsync()
    {
        if (config is null || HostId == 0)
            return;

        try
        {
            await Configuration.SaveConfigurationAsync(HostId, config, CancellationToken.None);
            config = await Configuration.LoadConfigurationAsync(HostId, CancellationToken.None);
            Toasts.Success("Settings saved.");
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            Toasts.Error(ex.Message);
        }
    }
}
