namespace BlokeBot.Features.Points.Giveaways;

public enum PointsGiveawayNotificationMode
{
    ReplyOnly,
    TwitchChat,
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

internal sealed class TwitchPointsGiveawaySchedulerNotification(
    ITwitchChatMessageSender sender
) : IPointsGiveawaySchedulerNotification
{
    public ValueTask SendAsync(
        PointsGiveawaySchedule schedule,
        string message,
        CancellationToken cancellationToken
    )
    {
        return schedule.Reply is { } reply
            ? reply(message, cancellationToken)
            : new ValueTask(
                sender.SendAsync(
                    schedule.HostLogin,
                    message,
                    new PublicChatDeliveryDeadline.ConfiguredMaximum(),
                    cancellationToken
                )
            );
    }
}
