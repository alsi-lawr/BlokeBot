namespace BlokeBot.Core.Features.Points.Gambling;

public sealed class PointsRandom : IPointsRandom
{
    private readonly Random _random = new();

    public double NextDouble()
    {
        return _random.NextDouble();
    }

    public int Next(int minValue, int maxValue)
    {
        return _random.Next(minValue, maxValue);
    }
}
