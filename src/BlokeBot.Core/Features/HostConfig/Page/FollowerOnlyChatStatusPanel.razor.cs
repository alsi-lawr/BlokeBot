using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class FollowerOnlyChatStatusPanel
{
    [Parameter, EditorRequired]
    public string HostLogin { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string BotAccountName { get; set; } = "the active bot account";

    [Parameter, EditorRequired]
    public string ReconnectUrl { get; set; } = "/oauth/start";

    [Parameter]
    public string? ReloadKey { get; set; }

    private FollowerOnlyChatReadiness? _readiness => BackgroundValue;

    private FollowerOnlyChatReadinessPresentation? _presentation =>
        _readiness is null ? null : FollowerOnlyChatReadinessPresentation.From(_readiness);

    private string _channelUrl => $"https://www.twitch.tv/{HostLogin}";

    protected override FollowerOnlyChatStatusPanelLoadIdentity? BackgroundLoadIdentity =>
        FollowerOnlyChatStatusPanelLoadIdentity.From(HostLogin, ReloadKey);

    protected override async Task<
        Result<FollowerOnlyChatReadiness, FollowerOnlyChatStatusPanelLoadFailure>
    > LoadBackgroundValueAsync(CancellationToken ct)
    {
        var result = await _followerOnlyChat.GetReadiness(HostLogin).ExecuteAsync(ct);
        return result.Match(
            Result<FollowerOnlyChatReadiness, FollowerOnlyChatStatusPanelLoadFailure>.Success,
            _ => throw new UnreachableException()
        );
    }

    private Task RecheckAsync()
    {
        ReloadKey = Guid.NewGuid().ToString("N");
        StateHasChanged();
        return Task.CompletedTask;
    }

    private string _badgeClass =>
        _presentation?.State switch
        {
            FollowerOnlyChatSetupState.NotRequired
            or FollowerOnlyChatSetupState.Exempt
            or FollowerOnlyChatSetupState.Eligible =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            FollowerOnlyChatSetupState.Waiting
            or FollowerOnlyChatSetupState.NotFollowing
            or FollowerOnlyChatSetupState.ReconnectRequired =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200",
            _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
        };

    private string _dotClass =>
        _presentation?.State switch
        {
            FollowerOnlyChatSetupState.NotRequired
            or FollowerOnlyChatSetupState.Exempt
            or FollowerOnlyChatSetupState.Eligible => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            FollowerOnlyChatSetupState.Waiting
            or FollowerOnlyChatSetupState.NotFollowing
            or FollowerOnlyChatSetupState.ReconnectRequired =>
                "h-1.5 w-1.5 rounded-full bg-amber-500",
            _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
        };

    private string _badgeText =>
        IsBackgroundLoading
            ? "checking"
            : _presentation?.State switch
            {
                FollowerOnlyChatSetupState.NotRequired => "not required",
                FollowerOnlyChatSetupState.Exempt => "exempt",
                FollowerOnlyChatSetupState.Eligible => "ready",
                FollowerOnlyChatSetupState.Waiting => "waiting",
                FollowerOnlyChatSetupState.NotFollowing => "follow needed",
                FollowerOnlyChatSetupState.ReconnectRequired => "reconnect",
                _ => "unknown",
            };
}

public sealed class FollowerOnlyChatStatusPanelLoadFailure { }
