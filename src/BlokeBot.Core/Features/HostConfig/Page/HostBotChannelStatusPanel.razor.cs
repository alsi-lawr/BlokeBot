using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using Microsoft.AspNetCore.Components;

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
            static _ => throw new UnreachableException()
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
        IsBackgroundLoading switch
        {
            true => "Checking whether the bot is a channel mod.",
            false => BackgroundError switch
            {
                { } error => error.ModeratorStatusMessage,
                _ => _status?.ModeratorStatusMessage
                    ?? "BlokeBot has not checked the bot account yet.",
            },
        };
}
