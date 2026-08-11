using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Competitions;

public sealed class CompetitionReminderDelivery(
    WhisperCommandResponseSender whispers,
    IDbContextFactory<BlokeBotDbContext> dbFactory
) : ICompetitionReminderDelivery
{
    public async Task<bool> DeliverAsync(
        string hostLogin,
        string message,
        IReadOnlyList<CompetitionReminderRecipient> recipients,
        CancellationToken cancellationToken
    )
    {
        if (
            !await HostFeatureAvailability.IsEnabledAsync(
                dbFactory,
                hostLogin,
                HostFeatureFlags.Competitions,
                cancellationToken
            )
        )
        {
            return false;
        }
        var delivered = false;
        foreach (var recipient in recipients)
        {
            var source = new ChatMessage(
                recipient.Login,
                hostLogin,
                string.Empty,
                string.Empty,
                new Dictionary<string, string> { ["user-id"] = recipient.TwitchUserId }
            );
            var result = await whispers.Deliver(source, message).ExecuteAsync(cancellationToken);
            delivered |= result.Match(_ => true, _ => false);
        }
        return delivered;
    }
}
