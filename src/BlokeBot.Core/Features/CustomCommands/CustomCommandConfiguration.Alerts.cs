using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandAlertSummary
{
    public int ActiveCount { get; set; }

    public List<CustomCommandAlertEditor> ActiveAlerts { get; set; } = [];
}

public sealed class CustomCommandAlertEditor
{
    public DurableAlertSeverity Severity { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? LinkPath { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
