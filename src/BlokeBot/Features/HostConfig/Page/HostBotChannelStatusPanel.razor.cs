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

    private HostBotChannelStatus? Status => BackgroundValue;

    protected override object? BackgroundLoadKey =>
        string.IsNullOrWhiteSpace(HostLogin) ? null : HostLogin.Trim().ToLowerInvariant();

    protected override Task<HostBotChannelStatus> LoadBackgroundValueAsync(CancellationToken ct) =>
        HostBotStatus.GetStatusAsync(HostLogin, ct);

    private string ModeratorStatusBadgeClass =>
        Status?.ModeratorState switch
        {
            HostBotModeratorState.IsModerator =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            HostBotModeratorState.NotModerator =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200",
            _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
        };

    private string ModeratorStatusDotClass =>
        Status?.ModeratorState switch
        {
            HostBotModeratorState.IsModerator => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            HostBotModeratorState.NotModerator => "h-1.5 w-1.5 rounded-full bg-amber-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string ModeratorStatusText =>
        IsBackgroundLoading
            ? "checking"
            : Status?.ModeratorState switch
            {
                HostBotModeratorState.IsModerator => "moderator",
                HostBotModeratorState.NotModerator => "not moderator",
                _ => "unknown",
            };

    private string ModeratorStatusMessage =>
        IsBackgroundLoading ? "Checking moderator status."
        : BackgroundError is not null ? "Moderator status could not be checked."
        : Status?.ModeratorStatusMessage ?? "Moderator status has not been checked yet.";

    private string FollowerReadStatusBadgeClass =>
        Status?.CanReadFollowers == true
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200"
        : Status?.ModeratorState == HostBotModeratorState.Unknown || IsBackgroundLoading
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200"
        : "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200";

    private string FollowerReadStatusDotClass =>
        Status?.CanReadFollowers == true ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
        : Status?.ModeratorState == HostBotModeratorState.Unknown || IsBackgroundLoading
            ? "h-1.5 w-1.5 rounded-full bg-slate-400"
        : "h-1.5 w-1.5 rounded-full bg-amber-500";

    private string FollowerReadStatusText =>
        IsBackgroundLoading ? "checking"
        : Status?.CanReadFollowers == true ? "can read"
        : Status?.ModeratorState == HostBotModeratorState.Unknown ? "unknown"
        : "not available";

    private string FollowerReadStatusMessage =>
        IsBackgroundLoading ? "Checking follower access."
        : BackgroundError is not null ? "Follower access could not be checked."
        : Status?.FollowerReadStatusMessage ?? "Follower access has not been checked yet.";
}
