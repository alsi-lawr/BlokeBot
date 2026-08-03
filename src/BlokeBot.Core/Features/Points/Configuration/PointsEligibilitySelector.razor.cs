using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

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
        IsBackgroundLoading switch
        {
            true => "Checking whether follower-only giveaways can work.",
            false => _followerEligibilityAvailable switch
            {
                true => "Followers can enter.",
                false => BackgroundError switch
                {
                    { } error => error.FollowerReadStatusMessage,
                    _ => _status?.ModeratorStatusMessage
                        ?? "Follower-only giveaways are not ready for this channel.",
                },
            },
        };

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
