using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Competitions;

public interface ICompetitionReminderWhisperSender
{
    Task<bool> DeliverAsync(
        ChatMessage source,
        string message,
        CancellationToken cancellationToken
    );
}

public sealed class CompetitionReminderWhisperSender(WhisperCommandResponseSender whispers)
    : ICompetitionReminderWhisperSender
{
    public async Task<bool> DeliverAsync(
        ChatMessage source,
        string message,
        CancellationToken cancellationToken
    )
    {
        var result = await whispers.Deliver(source, message).ExecuteAsync(cancellationToken);
        return result.Match(_ => true, _ => false);
    }
}

public sealed class CompetitionReminderDelivery(
    ICompetitionReminderWhisperSender whispers,
    IDbContextFactory<BlokeBotDbContext> dbFactory
) : ICompetitionReminderDelivery
{
    public async Task<bool> DeliverAsync(
        CompetitionReminderRequest request,
        CancellationToken cancellationToken
    )
    {
        var delivered = false;
        foreach (var recipient in request.Recipients)
        {
            if (!await CanDeliverAsync(request, cancellationToken))
            {
                return delivered;
            }
            var source = new ChatMessage(
                recipient.Login,
                request.HostLogin,
                string.Empty,
                string.Empty,
                new Dictionary<string, string> { ["user-id"] = recipient.TwitchUserId }
            );
            delivered |= await whispers.DeliverAsync(source, request.Message, cancellationToken);
        }
        return delivered;
    }

    private async Task<bool> CanDeliverAsync(
        CompetitionReminderRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                host =>
                    host.Id == request.HostId
                    && host.Login == request.HostLogin
                    && (host.EnabledFeatures & HostFeatureFlags.Competitions)
                        == HostFeatureFlags.Competitions
                    && (
                        host.CompetitionsAcceptWorkAfterUtc == null
                        || request.ReminderDueAtUtc >= host.CompetitionsAcceptWorkAfterUtc
                    ),
                cancellationToken
            );
    }
}
