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

namespace BlokeBot.Features.Points.Configuration;

public partial class PointsEligibilitySelector
{
    [Parameter, EditorRequired]
    public string HostLogin { get; set; } = string.Empty;

    [Parameter]
    public PointsEligibilityMode Value { get; set; }

    [Parameter]
    public EventCallback<PointsEligibilityMode> ValueChanged { get; set; }

    private HostBotChannelStatus? Status => BackgroundValue;

    protected override object? BackgroundLoadKey =>
        string.IsNullOrWhiteSpace(HostLogin) ? null : HostLogin.Trim().ToLowerInvariant();

    protected override Task<HostBotChannelStatus> LoadBackgroundValueAsync(CancellationToken ct) =>
        HostBotStatus.GetStatusAsync(HostLogin, ct);

    private bool FollowerEligibilityAvailable =>
        Status?.ModeratorState == HostBotModeratorState.IsModerator;

    private string FollowerEligibilityTitle =>
        IsBackgroundLoading ? "Checking moderator status for follower-only giveaways."
        : FollowerEligibilityAvailable ? "Followers"
        : BackgroundError is not null ? "Moderator status could not be checked."
        : Status?.ModeratorStatusMessage ?? "Moderator status is not ready for this channel.";

    private async Task OnEligibilityChangedAsync(ChangeEventArgs args)
    {
        if (
            !Enum.TryParse<PointsEligibilityMode>(
                args.Value?.ToString(),
                ignoreCase: true,
                out var mode
            )
        )
        {
            return;
        }

        if (mode == PointsEligibilityMode.Followers && !FollowerEligibilityAvailable)
            return;

        await ValueChanged.InvokeAsync(mode);
    }
}
