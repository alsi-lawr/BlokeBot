using BlokeBot.Features.Replies;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Components;

public partial class ReplyDeliverySettingsSection
{
    private const string WhisperDisabledTooltip =
        "Turn on whisper responses in Channel setup before sending replies as whispers.";

    [Parameter]
    public string Title { get; set; } = "Whisper replies";

    [Parameter]
    public string Description { get; set; } = "Choose which replies are sent privately.";

    [Parameter, EditorRequired]
    public ReplyDeliveryMap Delivery { get; set; } = new();

    [Parameter, EditorRequired]
    public IReadOnlyList<ReplyDeliveryOption> Options { get; set; } = [];

    [Parameter]
    public bool WhisperResponsesEnabled { get; set; }

    private bool WhisperDisabled => !WhisperResponsesEnabled;

    private string WhisperTitle => WhisperDisabled ? WhisperDisabledTooltip : string.Empty;

    private string WhisperLabelClass =>
        WhisperDisabled
            ? "inline-flex items-center gap-2 text-sm font-semibold text-muted-foreground opacity-60"
            : "inline-flex items-center gap-2 text-sm font-semibold text-muted-foreground";

    private void SetWhisper(string replyKey, ChangeEventArgs args)
    {
        if (WhisperDisabled)
            return;

        Delivery.SetWhisper(replyKey, args.Value is true || args.Value?.ToString() == "true");
    }
}
