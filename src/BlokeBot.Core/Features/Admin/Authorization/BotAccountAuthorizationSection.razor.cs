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
using BlokeBot.Core.Features.HostedChannels.Authorization;
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

namespace BlokeBot.Core.Features.Admin.Authorization;

public partial class BotAccountAuthorizationSection
{
    [Parameter]
    public BotAccountAuthorizationStatus? Status { get; set; }

    [Parameter]
    public string Title { get; set; } = "Bot account";

    [Parameter]
    public string Description { get; set; } =
        "Connect the Twitch account BlokeBot uses for chat and stream checks.";

    [Parameter]
    public string AuthorizeButtonText { get; set; } = "Connect bot account";

    [Parameter]
    public string AuthorizationStartUrl { get; set; } = "/oauth/start";

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public bool Collapsible { get; set; }

    [Parameter]
    public bool InitiallyOpen { get; set; } = true;

    [Parameter]
    public string? DisabledMessage { get; set; }

    [Parameter]
    public string ConfiguredAccountFallbackText { get; set; } = "not set";

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

    private string _authorizedAccountText =>
        Status?.AuthorizedLogin is { Length: > 0 } login
            ? $"@{login}"
            : "No Twitch account connected";

    private string _configuredAccountText =>
        Status?.ConfiguredBotLogin is { Length: > 0 } login
            ? $"@{login}"
            : ConfiguredAccountFallbackText;

    private string _sectionClass => Disabled ? "card bot-account-section--disabled" : "card";

    private string? _collapsibleSectionClass => Disabled ? "bot-account-section--disabled" : null;

    private string _bodyClass =>
        Disabled
            ? "grid gap-4 p-5 opacity-60 lg:grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)]"
            : "grid gap-4 p-5 lg:grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)]";

    private string _statusBadgeClass =>
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

    private string _statusDotClass =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled => "h-1.5 w-1.5 rounded-full bg-slate-400",
            BotAccountAuthorizationState.Ready => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            BotAccountAuthorizationState.WrongAccount
            or BotAccountAuthorizationState.MissingScopes =>
                "h-1.5 w-1.5 rounded-full bg-amber-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string _statusText =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled => "disabled",
            BotAccountAuthorizationState.Ready => "ready",
            BotAccountAuthorizationState.WrongAccount => "wrong account",
            BotAccountAuthorizationState.MissingScopes => "needs more access",
            BotAccountAuthorizationState.NotAuthorized => "not connected",
            _ => "unknown",
        };

    private string _connectionHelpText =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Disabled =>
                "Turn this on when you want to connect this account.",
            BotAccountAuthorizationState.Ready => "This account has everything BlokeBot needs.",
            BotAccountAuthorizationState.WrongAccount =>
                "Reconnect with the Twitch account shown above.",
            BotAccountAuthorizationState.MissingScopes =>
                "Reconnect this account so Twitch can give BlokeBot the access it needs.",
            BotAccountAuthorizationState.NotAuthorized => "Connect a Twitch account to continue.",
            _ => "Refresh to check this Twitch connection.",
        };

    private async Task SetEnableToggleAsync(ChangeEventArgs args)
    {
        await EnableToggleChanged(args.Value is true);
    }
}
