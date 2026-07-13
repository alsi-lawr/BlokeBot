using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.Points.Giveaways;

public enum PointsGiveawayNotificationMode
{
    ReplyOnly,
    PublicChat,
}

internal interface IPointsGiveawaySchedulerNotification
{
    ValueTask SendAsync(
        PointsGiveawaySchedule schedule,
        string message,
        CancellationToken cancellationToken
    );
}

internal sealed class ReplyOnlyPointsGiveawaySchedulerNotification
    : IPointsGiveawaySchedulerNotification
{
    public ValueTask SendAsync(
        PointsGiveawaySchedule schedule,
        string message,
        CancellationToken cancellationToken
    )
    {
        return schedule.Reply is { } reply
            ? reply(message, cancellationToken)
            : ValueTask.CompletedTask;
    }
}

internal sealed class PublicChatPointsGiveawaySchedulerNotification(
    IPublicChatMessageSender sender,
    ILogger<PublicChatPointsGiveawaySchedulerNotification> log
) : IPointsGiveawaySchedulerNotification
{
    public async ValueTask SendAsync(
        PointsGiveawaySchedule schedule,
        string message,
        CancellationToken cancellationToken
    )
    {
        if (schedule.Reply is { } reply)
        {
            await reply(message, cancellationToken);
            return;
        }

        var outcome = await sender.SendAsync(
            schedule.HostLogin,
            message,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        outcome
            .Match<Action>(
                static _ => static () => { },
                _ =>
                    () =>
                        log.LogWarning(
                            "Points giveaway notification for giveaway {GiveawayId} was rejected before durable public-chat enqueue; no delivery was attempted.",
                            schedule.GiveawayId
                        )
            )
            .Invoke();
    }
}
