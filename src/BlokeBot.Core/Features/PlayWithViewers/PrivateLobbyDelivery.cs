using BlokeBot.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PlayWithViewers;

public sealed record PrivateLobbyRecipient(string Login, string? TwitchUserId);

public sealed record PrivateLobbyDeliveryOutcome(string Login, bool Delivered, string? Failure);

public interface IPrivateLobbyDelivery
{
    Task<IReadOnlyList<PrivateLobbyDeliveryOutcome>> DeliverAsync(
        string hostLogin,
        string lobbyCode,
        IReadOnlyList<PrivateLobbyRecipient> recipients,
        CancellationToken ct
    );
}

public sealed class TwitchPrivateLobbyDelivery(
    WhisperCommandResponseSender whispers,
    IDbContextFactory<BlokeBotDbContext> dbFactory
) : IPrivateLobbyDelivery
{
    public async Task<IReadOnlyList<PrivateLobbyDeliveryOutcome>> DeliverAsync(
        string hostLogin,
        string lobbyCode,
        IReadOnlyList<PrivateLobbyRecipient> recipients,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostLogin);
        ArgumentException.ThrowIfNullOrWhiteSpace(lobbyCode);
        if (lobbyCode.Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(lobbyCode));
        }
        if (
            !await HostFeatureAvailability.IsEnabledAsync(
                dbFactory,
                hostLogin,
                HostFeatureFlags.PlayWithViewers,
                ct
            )
        )
        {
            return [];
        }

        var outcomes = new List<PrivateLobbyDeliveryOutcome>(recipients.Count);
        foreach (var recipient in recipients)
        {
            var tags = string.IsNullOrWhiteSpace(recipient.TwitchUserId)
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["user-id"] = recipient.TwitchUserId };
            var source = new ChatMessage(
                recipient.Login,
                hostLogin,
                string.Empty,
                string.Empty,
                tags
            );
            var result = await whispers.Deliver(source, lobbyCode).ExecuteAsync(ct);
            outcomes.Add(
                result.Match(
                    _ => new PrivateLobbyDeliveryOutcome(recipient.Login, true, null),
                    error => new PrivateLobbyDeliveryOutcome(
                        recipient.Login,
                        false,
                        error.GetType().Name
                    )
                )
            );
        }

        return outcomes;
    }
}
