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
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.Guessing.Rounds;

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
            return;

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
