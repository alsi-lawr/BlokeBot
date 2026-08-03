using System.Net;

namespace BlokeBot.Twitch;

public sealed class EventSubSubscriptionCreationException : HttpRequestException
{
    internal EventSubSubscriptionCreationException(
        HttpStatusCode statusCode,
        string? providerError,
        string? providerMessage,
        string? existingSubscriptionId
    )
        : base("Twitch rejected EventSub subscription creation.", null, statusCode)
    {
        ProviderError = providerError;
        ProviderMessage = providerMessage;
        ExistingSubscriptionId = existingSubscriptionId;
    }

    public string? ProviderError { get; }

    public string? ProviderMessage { get; }

    public string? ExistingSubscriptionId { get; }
}
