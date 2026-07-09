using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Components;
using BlokeBot.Components.Layout;
using BlokeBot.Eventing;
using BlokeBot.Features.AccessLists;
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

namespace BlokeBot.Features.Admin.SiteAccess;

public partial class SiteAccessSection
{
    [Parameter, EditorRequired]
    public Func<Task> AddBlacklist { get; set; } = () => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<Task> AddWhitelist { get; set; } = () => Task.CompletedTask;

    [Parameter]
    public IReadOnlyList<AccessListEntryProfile> BlacklistEntries { get; set; } = [];

    [Parameter]
    public string NewBlacklistLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewBlacklistLoginChanged { get; set; }

    [Parameter]
    public string NewWhitelistLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewWhitelistLoginChanged { get; set; }

    [Parameter, EditorRequired]
    public Func<string, Task> RemoveBlacklist { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<string, Task> RemoveWhitelist { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public SiteAccessAdminState State { get; set; } = new(false, [], []);

    [Parameter, EditorRequired]
    public Func<ChangeEventArgs, Task> ToggleWhitelist { get; set; } = _ => Task.CompletedTask;

    [Parameter]
    public IReadOnlyList<AccessListEntryProfile> WhitelistEntries { get; set; } = [];

    private async Task OnBlacklistInput(string value)
    {
        NewBlacklistLogin = value;
        await NewBlacklistLoginChanged.InvokeAsync(value);
    }

    private async Task OnWhitelistInput(string value)
    {
        NewWhitelistLogin = value;
        await NewWhitelistLoginChanged.InvokeAsync(value);
    }
}
