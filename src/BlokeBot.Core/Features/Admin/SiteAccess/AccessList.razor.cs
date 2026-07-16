using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.AccessLists;
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

namespace BlokeBot.Core.Features.Admin.SiteAccess;

public partial class AccessList
{
    private const int _removalAnimationDelayMs = 150;
    private readonly HashSet<string> _pendingRemovals = new(StringComparer.OrdinalIgnoreCase);

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

    private string _containerClass =>
        Disabled ? "surface-muted rounded-lg p-4 opacity-50" : "surface-muted rounded-lg p-4";

    private async Task OnInput(ChangeEventArgs args)
    {
        NewLogin = args.Value?.ToString() ?? string.Empty;
        await NewLoginChanged.InvokeAsync(NewLogin);
    }

    private string EntryRowClass(string entry)
    {
        const string BaseClass =
            "motion-list__item surface-row flex items-center justify-between rounded-md px-3 py-2";
        return _pendingRemovals.Contains(entry)
            ? $"{BaseClass} motion-list__item--removing"
            : BaseClass;
    }

    private async Task RemoveEntryAsync(string entry)
    {
        if (!_pendingRemovals.Add(entry))
        {
            return;
        }

        StateHasChanged();
        try
        {
            await Task.Delay(_removalAnimationDelayMs);
            await Remove(entry);
        }
        finally
        {
            _pendingRemovals.Remove(entry);
        }
    }
}
