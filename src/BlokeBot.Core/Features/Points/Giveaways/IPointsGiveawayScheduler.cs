namespace BlokeBot.Core.Features.Points.Giveaways;

public interface IPointsGiveawayScheduler
{
    void Schedule(PointsGiveawaySchedule schedule);

    void Cancel(int giveawayId);
}
