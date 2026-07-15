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

namespace BlokeBot.Features.Points.Configuration;

public partial class PointsEligibilitySelector
{
    [Parameter, EditorRequired]
    public string HostLogin { get; set; } = string.Empty;

    [Parameter]
    public PointsEligibilityMode Value { get; set; }

    [Parameter]
    public EventCallback<PointsEligibilityMode> ValueChanged { get; set; }

    private HostBotChannelStatus? _status => BackgroundValue;

    protected override PointsEligibilityLoadIdentity? BackgroundLoadIdentity =>
        PointsEligibilityLoadIdentity.From(HostLogin);

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

    private bool _followerEligibilityAvailable => _status?.IsModerator == true;

    private string _followerEligibilityTitle =>
        IsBackgroundLoading ? "Checking whether follower-only giveaways can work."
        : _followerEligibilityAvailable ? "Followers can enter."
        : BackgroundError is { } error ? error.FollowerReadStatusMessage
        : _status?.ModeratorStatusMessage
            ?? "Follower-only giveaways are not ready for this channel.";

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

        if (mode == PointsEligibilityMode.Followers && !_followerEligibilityAvailable)
        {
            return;
        }

        await ValueChanged.InvokeAsync(mode);
    }
}
