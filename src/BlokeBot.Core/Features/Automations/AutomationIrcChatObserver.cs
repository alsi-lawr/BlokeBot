using System.Collections.Immutable;
using System.Globalization;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationIrcChatObserver(
    ExpandedAutomationRuntime runtime,
    BotSettings settings
) : IChatMessageObserver
{
    public async ValueTask MessageReceivedAsync(ChatMessage message, CancellationToken cancellation)
    {
        if (
            settings.Runtime != ChatRuntime.Irc
            || !message.Tags.TryGetValue("room-id", out var channelId)
            || !message.Tags.TryGetValue("id", out var messageId)
            || !message.Tags.TryGetValue("user-id", out var viewerId)
            || !message.Tags.TryGetValue("tmi-sent-ts", out var timestamp)
            || !long.TryParse(
                timestamp,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var milliseconds
            )
            || milliseconds < 0
            || milliseconds > 253402300799999
        )
        {
            return;
        }
        var source = message.Tags.GetValueOrDefault("source-room-id") ?? channelId;
        var emotes = (message.Tags.GetValueOrDefault("emotes") ?? "")
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(group => group.Split(':', 2)[0])
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        await runtime.ChatReceivedAsync(
            new(
                messageId,
                DateTimeOffset.FromUnixTimeMilliseconds(milliseconds),
                channelId,
                source,
                viewerId,
                message.Login,
                message.Tags.GetValueOrDefault("display-name") ?? message.Login,
                message.Text,
                emotes
            ),
            cancellation
        );
    }
}
