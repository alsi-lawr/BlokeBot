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
using BlokeBot.Features.Replies;
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

namespace BlokeBot.Features.Guessing.Configuration;

public partial class GuessOptionsSettingsSection
{
    private const int _removalAnimationDelayMs = 150;
    private const string _whisperDisabledTooltip =
        "Enable whisper responses in Channel setup before using whisper replies.";
    private readonly HashSet<GuessOptionEditor> _pendingRemovals = [];

    [Parameter, EditorRequired]
    public EventCallback AddOption { get; set; }

    [Parameter, EditorRequired]
    public List<GuessOptionEditor> Options { get; set; } = [];

    [Parameter]
    public bool WhisperResponsesEnabled { get; set; }

    [Parameter]
    public bool WhisperAnswerReplies { get; set; }

    [Parameter]
    public EventCallback<bool> WhisperAnswerRepliesChanged { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<GuessOptionEditor> RemoveOption { get; set; }

    private bool _whisperDisabled => !WhisperResponsesEnabled;

    private string _whisperTitle => _whisperDisabled ? _whisperDisabledTooltip : string.Empty;

    private string _whisperLabelClass =>
        _whisperDisabled
            ? "inline-flex items-center gap-2 text-xs font-semibold text-muted-foreground opacity-60"
            : "inline-flex items-center gap-2 text-xs font-semibold text-muted-foreground";

    private async Task InvokeAddOptionAsync()
    {
        await AddOption.InvokeAsync();
    }

    private async Task SetAnswerWhispersAsync(ChangeEventArgs args)
    {
        if (_whisperDisabled)
        {
            return;
        }

        var whisper = args.Value is true || args.Value?.ToString() == "true";
        WhisperAnswerReplies = whisper;
        ApplyAnswerTarget(whisper);
        await WhisperAnswerRepliesChanged.InvokeAsync(whisper);
    }

    private void ApplyAnswerTarget(bool whisper)
    {
        var target = whisper ? ReplyDeliveryTarget.Whisper : ReplyDeliveryTarget.Chat;

        foreach (var option in Options)
        {
            option.ReplyTarget = target;
        }
    }

    private string OptionRowClass(GuessOptionEditor option)
    {
        const string BaseClass =
            "motion-list__item surface-muted grid gap-3 rounded-lg p-3 lg:grid-cols-[0.45fr_1fr_auto]";
        return _pendingRemovals.Contains(option)
            ? $"{BaseClass} motion-list__item--removing"
            : BaseClass;
    }

    private async Task RemoveOptionAsync(GuessOptionEditor option)
    {
        if (!_pendingRemovals.Add(option))
        {
            return;
        }

        StateHasChanged();
        try
        {
            await Task.Delay(_removalAnimationDelayMs);
            await RemoveOption.InvokeAsync(option);
        }
        finally
        {
            _pendingRemovals.Remove(option);
        }
    }
}
