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

namespace BlokeBot.Core.Features.Guessing.Rounds;

public partial class RoundOrchestrationPanel
{
    [Parameter, EditorRequired]
    public EventCallback DeclareWinner { get; set; }

    [Parameter, EditorRequired]
    public int SelectedProfileId { get; set; }

    [Parameter]
    public EventCallback<int> SelectedProfileIdChanged { get; set; }

    [Parameter, EditorRequired]
    public EventCallback StartRound { get; set; }

    [Parameter, EditorRequired]
    public GuessingDashboardState State { get; set; } = new();

    [Parameter, EditorRequired]
    public EventCallback StopGuessing { get; set; }

    [Parameter]
    public string WinnerName { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> WinnerNameChanged { get; set; }

    private async Task OnSelectedProfileChanged(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out var profileId))
        {
            return;
        }

        SelectedProfileId = profileId;
        await SelectedProfileIdChanged.InvokeAsync(profileId);
    }

    private async Task OnWinnerNameChanged(ChangeEventArgs args)
    {
        WinnerName = args.Value?.ToString() ?? string.Empty;
        await WinnerNameChanged.InvokeAsync(WinnerName);
    }

    private async Task InvokeDeclareWinnerAsync() => await DeclareWinner.InvokeAsync();

    private async Task InvokeStartRoundAsync() => await StartRound.InvokeAsync();

    private async Task InvokeStopGuessingAsync() => await StopGuessing.InvokeAsync();
}
