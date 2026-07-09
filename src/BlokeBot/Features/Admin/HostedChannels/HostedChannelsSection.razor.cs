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
using BlokeBot.Features.Admin.Monitoring;
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

namespace BlokeBot.Features.Admin.HostedChannels;

public partial class HostedChannelsSection
{
    [Parameter, EditorRequired]
    public Func<Task> CreateHost { get; set; } = () => Task.CompletedTask;

    [Parameter]
    public bool CanEditHosts { get; set; } = true;

    [Parameter, EditorRequired]
    public IReadOnlyList<HostedChannelAdminView> Hosts { get; set; } = [];

    [Parameter]
    public string NewHostLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewHostLoginChanged { get; set; }

    [Parameter, EditorRequired]
    public Func<int, Task> RemoveHost { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StartBot { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<int, Task> StopBot { get; set; } = _ => Task.CompletedTask;

    private async Task OnInput(ChangeEventArgs args)
    {
        NewHostLogin = args.Value?.ToString() ?? string.Empty;
        await NewHostLoginChanged.InvokeAsync(NewHostLogin);
    }
}
