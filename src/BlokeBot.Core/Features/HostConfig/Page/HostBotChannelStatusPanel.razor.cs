using System.Diagnostics;
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

namespace BlokeBot.Core.Features.HostConfig.Page;

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
                "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200",
            { ModeratorCheckCompleted: true } =>
                "status-pill bg-amber-50 text-amber-700 ring-1 ring-amber-200",
            _ => "status-pill bg-slate-100 text-slate-600 ring-1 ring-slate-200",
        };

    private string _moderatorStatusDotClass =>
        _status switch
        {
            { IsModerator: true } => "status-pill__dot bg-emerald-500",
            { ModeratorCheckCompleted: true } => "status-pill__dot bg-amber-500",
            _ => "status-pill__dot bg-slate-400",
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
            ? "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200"
        : _status?.ModeratorCheckCompleted != true || IsBackgroundLoading
            ? "status-pill bg-slate-100 text-slate-600 ring-1 ring-slate-200"
        : "status-pill bg-amber-50 text-amber-700 ring-1 ring-amber-200";

    private string _followerReadStatusDotClass =>
        _status?.CanReadFollowers == true ? "status-pill__dot bg-emerald-500"
        : _status?.ModeratorCheckCompleted != true || IsBackgroundLoading
            ? "status-pill__dot bg-slate-400"
        : "status-pill__dot bg-amber-500";

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
