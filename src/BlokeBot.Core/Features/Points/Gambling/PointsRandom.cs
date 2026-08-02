namespace BlokeBot.Core.Features.Points.Gambling;

public sealed class PointsRandom : IPointsRandom
{
    private readonly Random _random = new();

    public double NextDouble() => _random.NextDouble();

    public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);
}
