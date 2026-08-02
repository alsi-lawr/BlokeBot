using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public partial class GuessOptionsSettingsSection
{
    private const int _removalAnimationDelayMs = 150;
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

    private string _whisperLabelClass =>
        _whisperDisabled
            ? "inline-flex items-center gap-2 text-xs font-semibold text-muted-foreground opacity-60"
            : "inline-flex items-center gap-2 text-xs font-semibold text-muted-foreground";

    private async Task InvokeAddOptionAsync() => await AddOption.InvokeAsync();

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
