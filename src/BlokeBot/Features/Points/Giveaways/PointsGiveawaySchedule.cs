namespace BlokeBot.Features.Points.Giveaways;

public sealed record PointsGiveawaySchedule(
    int GiveawayId,
    int HostId,
    string HostLogin,
    DateTime StartedAtUtc,
    DateTime EndsAtUtc,
    Func<string, CancellationToken, ValueTask>? Reply
);
