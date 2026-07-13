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

namespace BlokeBot.Features.HostConfig.Page;

public partial class HostBotChannelStatusPanel
{
    [Parameter, EditorRequired]
    public string HostLogin { get; set; } = string.Empty;

    [Parameter]
    public string? ReloadKey { get; set; }

    private HostBotChannelStatus? _status => BackgroundValue;

    protected override object? BackgroundLoadKey =>
        string.IsNullOrWhiteSpace(HostLogin)
            ? null
            : $"{HostLogin.Trim().ToLowerInvariant()}:{ReloadKey}";

    protected override Task<HostBotChannelStatus> LoadBackgroundValueAsync(CancellationToken ct)
    {
        return _hostBotStatus.GetStatusAsync(HostLogin, ct);
    }

    private string _moderatorStatusBadgeClass =>
        _status?.ModeratorState switch
        {
            HostBotModeratorState.IsModerator =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            HostBotModeratorState.NotModerator =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200",
            _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
        };

    private string _moderatorStatusDotClass =>
        _status?.ModeratorState switch
        {
            HostBotModeratorState.IsModerator => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            HostBotModeratorState.NotModerator => "h-1.5 w-1.5 rounded-full bg-amber-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string _moderatorStatusText =>
        IsBackgroundLoading
            ? "checking"
            : _status?.ModeratorState switch
            {
                HostBotModeratorState.IsModerator => "yes",
                HostBotModeratorState.NotModerator => "no",
                _ => "unknown",
            };

    private string _moderatorStatusMessage =>
        IsBackgroundLoading ? "Checking whether the bot is a channel mod."
        : BackgroundError is not null ? "BlokeBot could not check whether the bot is a mod."
        : _status?.ModeratorStatusMessage ?? "BlokeBot has not checked the bot account yet.";

    private string _followerReadStatusBadgeClass =>
        _status?.CanReadFollowers == true
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200"
        : _status?.ModeratorState == HostBotModeratorState.Unknown || IsBackgroundLoading
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200"
        : "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200";

    private string _followerReadStatusDotClass =>
        _status?.CanReadFollowers == true ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
        : _status?.ModeratorState == HostBotModeratorState.Unknown || IsBackgroundLoading
            ? "h-1.5 w-1.5 rounded-full bg-slate-400"
        : "h-1.5 w-1.5 rounded-full bg-amber-500";

    private string _followerReadStatusText =>
        IsBackgroundLoading ? "checking"
        : _status?.CanReadFollowers == true ? "ready"
        : _status?.ModeratorState == HostBotModeratorState.Unknown ? "unknown"
        : "not ready";

    private string _followerReadStatusMessage =>
        IsBackgroundLoading ? "Checking whether follower-only giveaways can work."
        : BackgroundError is not null ? "BlokeBot could not check follower-only giveaways."
        : _status?.FollowerReadStatusMessage
            ?? "BlokeBot has not checked follower-only giveaways yet.";
}
