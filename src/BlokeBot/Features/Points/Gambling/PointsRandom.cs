namespace BlokeBot.Features.Points.Gambling;

public sealed class PointsRandom : IPointsRandom
{
    private readonly Random random = new();

    public double NextDouble() => random.NextDouble();

    public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
}
