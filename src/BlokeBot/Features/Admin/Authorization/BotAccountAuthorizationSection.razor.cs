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
using BlokeBot.Features.HostedChannels.Authorization;
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

namespace BlokeBot.Features.Admin.Authorization;

public partial class BotAccountAuthorizationSection
{
    private IJSObjectReference? authorizationModule;
    private bool authorizationOpening;

    [Inject]
    public IJSRuntime Js { get; set; } = default!;

    [Parameter]
    public BotAccountAuthorizationStatus? Status { get; set; }

    [Parameter]
    public string Title { get; set; } = "Bot account authorization";

    [Parameter]
    public string Description { get; set; } =
        "Authorize the Twitch account BlokeBot uses for chat and API checks.";

    [Parameter]
    public string AuthorizeButtonText { get; set; } = "Authorize bot account";

    [Parameter]
    public string AuthorizationStartUrl { get; set; } = "/oauth/start";

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public string? DisabledMessage { get; set; }

    [Parameter]
    public string ConfiguredAccountFallbackText { get; set; } = "not configured";

    [Parameter]
    public bool ShowEnableToggle { get; set; }

    [Parameter]
    public bool EnableToggleValue { get; set; }

    [Parameter]
    public string EnableToggleLabel { get; set; } = "Enable";

    [Parameter]
    public Func<bool, Task> EnableToggleChanged { get; set; } = _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<Task> Clear { get; set; } = () => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<Task> Refresh { get; set; } = () => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (authorizationModule is null)
            return;

        try
        {
            await authorizationModule.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private async Task OpenAuthorizationAsync()
    {
        if (authorizationOpening)
            return;

        authorizationOpening = true;
        try
        {
            authorizationModule ??= await Js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Features/Admin/Authorization/BotAccountAuthorizationSection.razor.js"
            );
            var popupClosed = await authorizationModule.InvokeAsync<bool>(
                "openBotAuthorization",
                AuthorizationStartUrl
            );
            if (popupClosed)
                await Refresh();
        }
        finally
        {
            authorizationOpening = false;
        }
    }

    private string AuthorizedAccountText =>
        Status?.AuthorizedLogin is { Length: > 0 } login
            ? $"@{login}"
            : "No saved Twitch authorization";

    private string ConfiguredAccountText =>
        Status?.ConfiguredBotLogin is { Length: > 0 } login
            ? $"@{login}"
            : ConfiguredAccountFallbackText;

    private string SectionClass => Disabled ? "card bot-account-section--disabled" : "card";

    private string BodyClass =>
        Disabled
            ? "grid gap-4 p-5 opacity-60 lg:grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)]"
            : "grid gap-4 p-5 lg:grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)]";

    private string StatusBadgeClass =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
            BotAccountAuthorizationState.Ready =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            BotAccountAuthorizationState.WrongAccount
            or BotAccountAuthorizationState.MissingScopes =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200",
            _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
        };

    private string StatusDotClass =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled => "h-1.5 w-1.5 rounded-full bg-slate-400",
            BotAccountAuthorizationState.Ready => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            BotAccountAuthorizationState.WrongAccount
            or BotAccountAuthorizationState.MissingScopes =>
                "h-1.5 w-1.5 rounded-full bg-amber-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string StatusText =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled => "disabled",
            BotAccountAuthorizationState.Ready => "current",
            BotAccountAuthorizationState.WrongAccount => "wrong account",
            BotAccountAuthorizationState.MissingScopes => "missing permissions",
            BotAccountAuthorizationState.NotAuthorized => "not authorized",
            _ => "unknown",
        };

    private static string FormatScopes(IReadOnlyList<string>? scopes) =>
        scopes is { Count: > 0 } ? string.Join(", ", scopes) : "none";

    private async Task SetEnableToggleAsync(ChangeEventArgs args)
    {
        await EnableToggleChanged(args.Value is true);
    }
}
