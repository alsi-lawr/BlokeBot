using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One rendered line of a <see cref="StudioChatPreview"/>. A line with no speaker is narration
/// explaining why the command produces no chat message.
/// </summary>
public sealed record StudioChatLine
{
    public required string Message { get; init; }

    public string? Speaker { get; init; }

    public string SpeakerColour { get; init; } = "#9146ff";

    public string? Badge { get; init; }

    public bool Bot { get; init; }

    public bool Monospace { get; init; }
}

/// <summary>
/// A static Twitch-style chat excerpt showing configured replies in conversational context. It
/// renders what is already configured and never animates, types, or contacts chat.
/// </summary>
public partial class StudioChatPreview
{
    [Parameter, EditorRequired]
    public required IReadOnlyList<StudioChatLine> Lines { get; set; }

    [Parameter]
    public string Heading { get; set; } = "Viewers will see";

    private static string LineClass(StudioChatLine line) =>
        line.Bot
            ? "studio-chat__line studio-chat__line--bot -mx-[0.45rem] mt-[0.15rem] rounded-lg bg-[var(--app-affirmative-surface)] px-[0.45rem] py-1 text-[var(--app-text-strong)] wrap-anywhere"
        : line.Speaker is null
            ? "studio-chat__line studio-chat__line--note py-[0.12rem] text-[var(--app-text-muted)] wrap-anywhere"
        : "studio-chat__line py-[0.12rem] text-[var(--app-text-strong)] wrap-anywhere";

    private static string LineKind(StudioChatLine line) =>
        line.Bot ? "bot"
        : line.Speaker is null ? "note"
        : "viewer";
}
