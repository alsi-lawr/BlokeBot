using BlokeBot.Core.Features.Guessing.Game;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardGuessingReplies(
    IPublicChatMessageSender sender,
    ILogger<DashboardGuessingReplies> log
)
{
    internal async Task<string?> QueueAsync(string channel, GuessingOperationOutcome result)
    {
        if (
            result is not GuessingOperationOutcome.Succeeded
            || string.IsNullOrWhiteSpace(result.Message)
        )
        {
            return null;
        }
        if (result.Target != CommandResponseTarget.Chat)
        {
            return "The action completed, but its reply is not a public chat message.";
        }
        try
        {
            var queued = result.Pin is { } pin
                ? await sender.SendAsync(
                    channel,
                    result.Message,
                    new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                    pin,
                    CancellationToken.None
                )
                : await sender.SendAsync(
                    channel,
                    result.Message,
                    new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                    CancellationToken.None
                );
            return queued.Match<string?>(
                _ => null,
                _ => "The action completed, but its chat message could not be queued."
            );
        }
        catch (Exception exception)
        {
            log.LogError(
                exception,
                "Guessing dashboard action completed but queue admission failed for channel {Channel}.",
                channel
            );
            return "The action completed, but its chat message could not be queued.";
        }
    }
}
