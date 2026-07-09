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
    [Parameter]
    public BotAccountAuthorizationStatus? Status { get; set; }

    [Parameter, EditorRequired]
    public Func<Task> Clear { get; set; } = () => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<Task> Refresh { get; set; } = () => Task.CompletedTask;

    private string StatusBadgeClass =>
        Status?.State switch
        {
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
            BotAccountAuthorizationState.Ready => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            BotAccountAuthorizationState.WrongAccount
            or BotAccountAuthorizationState.MissingScopes =>
                "h-1.5 w-1.5 rounded-full bg-amber-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string StatusText =>
        Status?.State switch
        {
            BotAccountAuthorizationState.Ready => "current",
            BotAccountAuthorizationState.WrongAccount => "wrong account",
            BotAccountAuthorizationState.MissingScopes => "missing permissions",
            BotAccountAuthorizationState.NotAuthorized => "not authorized",
            _ => "unknown",
        };

    private static string FormatScopes(IReadOnlyList<string>? scopes) =>
        scopes is { Count: > 0 } ? string.Join(", ", scopes) : "none";
}
