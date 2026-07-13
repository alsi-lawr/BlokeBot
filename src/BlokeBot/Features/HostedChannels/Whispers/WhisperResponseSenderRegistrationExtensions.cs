namespace BlokeBot.Features.HostedChannels.Whispers;

/// <summary>
/// Configures the hosted whisper response sender owned by the BlokeBot feature.
/// </summary>
public static class WhisperResponseSenderRegistrationExtensions
{
    /// <summary>
    /// Uses hosted-channel whisper delivery for private command responses.
    /// </summary>
    public static IChatBotBuilder UseWhisperCommandResponseSender(this IChatBotBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.OverrideCommandResponseSenderWith<WhisperCommandResponseSender>();
    }
}
