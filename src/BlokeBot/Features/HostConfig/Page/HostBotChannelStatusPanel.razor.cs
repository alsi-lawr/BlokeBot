using System.Diagnostics;
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
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using static Microsoft.AspNetCore.Components.Web.RenderMode;

namespace BlokeBot.Features.HostConfig.Page;

public partial class HostBotChannelStatusPanel
{
    [Parameter, EditorRequired]
    public string HostLogin { get; set; } = string.Empty;

    [Parameter]
    public string? ReloadKey { get; set; }

    private HostBotChannelStatus? _status => BackgroundValue;

    protected override HostBotChannelStatusPanelLoadIdentity? BackgroundLoadIdentity =>
        HostBotChannelStatusPanelLoadIdentity.From(HostLogin, ReloadKey);

    protected override async Task<
        Result<HostBotChannelStatus, HostBotChannelStatusLoadFailure>
    > LoadBackgroundValueAsync(CancellationToken ct)
    {
        var result = await _hostBotStatus.GetReadiness(HostLogin).ExecuteAsync(ct);
        return result.Match(
            HostBotChannelStatusLoadFailure.FromReadiness,
            _ => throw new UnreachableException()
        );
    }

    private string _moderatorStatusBadgeClass =>
        _status switch
        {
            { IsModerator: true } =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            { ModeratorCheckCompleted: true } =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200",
            _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
        };

    private string _moderatorStatusDotClass =>
        _status switch
        {
            { IsModerator: true } => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            { ModeratorCheckCompleted: true } => "h-1.5 w-1.5 rounded-full bg-amber-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string _moderatorStatusText =>
        IsBackgroundLoading
            ? "checking"
            : _status switch
            {
                { IsModerator: true } => "yes",
                { ModeratorCheckCompleted: true } => "no",
                _ => "unknown",
            };

    private string _moderatorStatusMessage =>
        IsBackgroundLoading ? "Checking whether the bot is a channel mod."
        : BackgroundError is { } error ? error.ModeratorStatusMessage
        : _status?.ModeratorStatusMessage ?? "BlokeBot has not checked the bot account yet.";

    private string _followerReadStatusBadgeClass =>
        _status?.CanReadFollowers == true
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200"
        : _status?.ModeratorCheckCompleted != true || IsBackgroundLoading
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200"
        : "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200";

    private string _followerReadStatusDotClass =>
        _status?.CanReadFollowers == true ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
        : _status?.ModeratorCheckCompleted != true || IsBackgroundLoading
            ? "h-1.5 w-1.5 rounded-full bg-slate-400"
        : "h-1.5 w-1.5 rounded-full bg-amber-500";

    private string _followerReadStatusText =>
        IsBackgroundLoading ? "checking"
        : _status?.CanReadFollowers == true ? "ready"
        : _status?.ModeratorCheckCompleted != true ? "unknown"
        : "not ready";

    private string _followerReadStatusMessage =>
        IsBackgroundLoading ? "Checking whether follower-only giveaways can work."
        : BackgroundError is { } error ? error.FollowerReadStatusMessage
        : _status?.FollowerReadStatusMessage
            ?? "BlokeBot has not checked follower-only giveaways yet.";
}
