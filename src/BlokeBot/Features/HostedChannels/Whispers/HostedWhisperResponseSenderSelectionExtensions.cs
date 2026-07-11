namespace BlokeBot.Features.HostedChannels.Whispers;

/// <summary>
/// Selects the hosted whisper response sender owned by the BlokeBot feature.
/// </summary>
public static class HostedWhisperResponseSenderSelectionExtensions
{
    /// <summary>
    /// Uses hosted-channel whisper delivery with public-chat fallback.
    /// </summary>
    public static TwitchCommandResponseSenderSelection UseBlokeBotHostedWhisperSender(
        this TwitchCommandResponseSenderSelection selection
    )
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.UseHostedWhisperSender<HostWhisperCommandResponseSender>();
    }
}
