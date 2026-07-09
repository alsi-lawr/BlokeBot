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

public partial class AccessList
{
    private const int RemovalAnimationDelayMs = 150;
    private readonly HashSet<string> pendingRemovals = new(StringComparer.OrdinalIgnoreCase);

    [Parameter]
    public Func<Task> Add { get; set; } = () => Task.CompletedTask;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public IReadOnlyList<AccessListEntryProfile> Entries { get; set; } = [];

    [Parameter]
    public string NewLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewLoginChanged { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = string.Empty;

    [Parameter]
    public Func<string, Task> Remove { get; set; } = _ => Task.CompletedTask;

    [Parameter]
    public string Title { get; set; } = string.Empty;

    private string ContainerClass =>
        Disabled ? "surface-muted rounded-lg p-4 opacity-50" : "surface-muted rounded-lg p-4";

    private async Task OnInput(ChangeEventArgs args)
    {
        NewLogin = args.Value?.ToString() ?? string.Empty;
        await NewLoginChanged.InvokeAsync(NewLogin);
    }

    private string EntryRowClass(string entry)
    {
        const string baseClass =
            "motion-list__item surface-row flex items-center justify-between rounded-md px-3 py-2";
        return pendingRemovals.Contains(entry)
            ? $"{baseClass} motion-list__item--removing"
            : baseClass;
    }

    private async Task RemoveEntryAsync(string entry)
    {
        if (!pendingRemovals.Add(entry))
            return;

        StateHasChanged();
        try
        {
            await Task.Delay(RemovalAnimationDelayMs);
            await Remove(entry);
        }
        finally
        {
            pendingRemovals.Remove(entry);
        }
    }
}
