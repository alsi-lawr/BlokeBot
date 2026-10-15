using Cel;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationCelFunctions
{
    internal const string JoinName = "join";

    internal static CelEnvironment CreateEnvironment()
    {
        var environment = new CelEnvironment([], string.Empty);
        environment.RegisterFunction(
            JoinName,
            [typeof(string[]), typeof(long), typeof(long), typeof(string)],
            static values =>
                Join((string[])values[0]!, (long)values[1]!, (long)values[2]!, (string)values[3]!)
        );
        environment.RegisterFunction(
            JoinName,
            [typeof(string[]), typeof(long), typeof(int), typeof(string)],
            static values =>
                Join((string[])values[0]!, (long)values[1]!, (int)values[2]!, (string)values[3]!)
        );
        environment.RegisterFunction(
            JoinName,
            [typeof(string[]), typeof(int), typeof(long), typeof(string)],
            static values =>
                Join((string[])values[0]!, (int)values[1]!, (long)values[2]!, (string)values[3]!)
        );
        environment.RegisterFunction(
            JoinName,
            [typeof(string[]), typeof(int), typeof(int), typeof(string)],
            static values =>
                Join((string[])values[0]!, (int)values[1]!, (int)values[2]!, (string)values[3]!)
        );
        return environment;
    }

    private static string Join(string[] arguments, long start, long end, string separator)
    {
        var first = (int)Math.Clamp(start, 0L, arguments.LongLength);
        var limit = (int)Math.Clamp(end, 0L, arguments.LongLength);
        return limit <= first
            ? string.Empty
            : string.Join(separator, arguments, first, limit - first);
    }
}
