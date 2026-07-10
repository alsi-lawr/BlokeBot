namespace BlokeBot.Persistence.Models;

public enum DurableAlertSeverity
{
    Info,
    Warning,
    Critical,
}

public static class DurableAlertSeverityStore
{
    public static IReadOnlyList<string> Values { get; } =
    [
        Format(DurableAlertSeverity.Critical),
        Format(DurableAlertSeverity.Info),
        Format(DurableAlertSeverity.Warning),
    ];

    public static string Format(DurableAlertSeverity severity) => severity.ToString();

    public static DurableAlertSeverity Parse(string value) =>
        Enum.TryParse<DurableAlertSeverity>(value, ignoreCase: true, out var severity)
            ? severity
            : throw new FormatException($"Unknown durable alert severity '{value}'.");
}
