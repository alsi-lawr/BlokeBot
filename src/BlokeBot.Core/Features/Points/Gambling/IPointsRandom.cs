namespace BlokeBot.Core.Features.Points.Gambling;

public interface IPointsRandom
{
    double NextDouble();

    int Next(int minValue, int maxValue);
}
