namespace BlokeBot.Simulation;

internal static class SimulationMode
{
    public const string EnvironmentName = "Simulation";
    public const string UserId = "simulation-user";
    public const string Login = "samplechannel";
    public const string DisplayName = "Sample Channel";

    public static DateTimeOffset Now { get; } = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
}
