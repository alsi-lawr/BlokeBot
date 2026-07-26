using System.Text.Json;

namespace BlokeBot.Twitch.Runtime;

internal abstract record EventSubNotification
{
    private EventSubNotification() { }

    internal sealed record Chat(EventSubChatMessageEvent Event) : EventSubNotification;

    internal sealed record Shoutout(EventSubShoutoutEvent Event) : EventSubNotification;

    internal sealed record Unknown : EventSubNotification;

    internal static EventSubNotification Parse(
        EventSubEnvelope envelope,
        JsonSerializerOptions options
    )
    {
        if (envelope.Payload.Event is not { } payload)
        {
            return new Unknown();
        }
        return envelope.Metadata.SubscriptionType switch
        {
            "" or "channel.chat.message" => payload.Deserialize<EventSubChatMessageEvent>(options)
                is { } chat
                ? new Chat(chat)
                : new Unknown(),
            "channel.shoutout.create" => payload.Deserialize<EventSubShoutoutWireEvent>(options)
                is { } shoutout
                ? new Shoutout(
                    shoutout.ToDomain(EventSubShoutoutDirection.Sent, envelope.Metadata.MessageId)
                )
                : new Unknown(),
            "channel.shoutout.receive" => payload.Deserialize<EventSubShoutoutWireEvent>(options)
                is { } shoutout
                ? new Shoutout(
                    shoutout.ToDomain(
                        EventSubShoutoutDirection.Received,
                        envelope.Metadata.MessageId
                    )
                )
                : new Unknown(),
            _ => new Unknown(),
        };
    }
}
