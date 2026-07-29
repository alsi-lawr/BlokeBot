using System.Security.Cryptography;
using System.Text;

namespace BlokeBot.Twitch.Runtime;

internal static class PublicChatMessageDeduplication
{
    public static PublicChatDeduplicationKey Key(string channel, string message)
    {
        var normalizedChannel = channel.Trim().ToLowerInvariant();
        var normalizedMessage = message.Trim();
        var material = $"{normalizedChannel.Length}:{normalizedChannel}{normalizedMessage}";
        return new PublicChatDeduplicationKey(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
        );
    }

    public static PublicChatDeduplicationKey CorrelatedKey(
        PublicChatDeliveryCorrelation correlation
    )
    {
        var validated = correlation.Validate();
        return Hash($"automatic-raid:{validated.HostId}:{validated.ProviderMessageId}");
    }

    private static PublicChatDeduplicationKey Hash(string material)
    {
        return new PublicChatDeduplicationKey(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
        );
    }
}
