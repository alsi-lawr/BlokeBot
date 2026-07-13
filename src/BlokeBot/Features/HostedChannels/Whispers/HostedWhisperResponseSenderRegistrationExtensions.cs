namespace BlokeBot.Features.HostedChannels.Whispers;

/// <summary>
/// Configures the hosted whisper response sender owned by the BlokeBot feature.
/// </summary>
public static class HostedWhisperResponseSenderRegistrationExtensions
{
    /// <summary>
    /// Uses hosted-channel whisper delivery for private command responses.
    /// </summary>
    public static ITwitchBotBuilder UseBlokeBotHostedWhisperSender(this ITwitchBotBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.OverrideCommandResponseSenderWith<HostWhisperCommandResponseSender>();
    }
}
