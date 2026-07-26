namespace BlokeBot.Simulation;

internal static class SimulationMode
{
    public const string EnvironmentName = "Simulation";

    public static DateTimeOffset Now { get; } = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    public static FakeTwitch.FakeTwitchScenarioDefinition SelectScenario(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var names = arguments
            .Select((argument, index) => (argument, index))
            .Where(value => value.argument == "--simulation-scenario")
            .Select(value => value.index + 1 < arguments.Length ? arguments[value.index + 1] : null)
            .ToArray();
        if (
            names.Length > 1
            || names.SingleOrDefault() is { } name
                && !string.Equals(
                    name,
                    FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboardName,
                    StringComparison.Ordinal
                )
        )
        {
            throw new InvalidOperationException(
                "Simulation requires the allowlisted ready-dashboard scenario."
            );
        }

        return FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard;
    }
}
