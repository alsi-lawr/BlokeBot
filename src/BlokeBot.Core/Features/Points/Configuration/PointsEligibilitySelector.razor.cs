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

namespace BlokeBot.Core.Features.Points.Configuration;

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
