namespace BlokeBot.Persistence.Models;

public sealed class AutomationCountdown
{
    public int HostId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OccurrenceId { get; set; }
    public Guid ProcessId { get; set; }
    public int AutomationGeneration { get; set; }
    public bool IsRunning { get; set; }
    public bool WasCancelled { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime DeadlineUtc { get; set; }
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class AutomationStreamObservation
{
    public int HostId { get; set; }
    public string StreamId { get; set; } = string.Empty;
    public DateTime? SuppressUptimeBeforeUtc { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class AutomationSeenViewer
{
    public int HostId { get; set; }
    public string ViewerId { get; set; } = string.Empty;
}

public sealed class AutomationGoalObservation
{
    public int HostId { get; set; }
    public string GoalId { get; set; } = string.Empty;
    public long CurrentAmount { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public Guid ConnectionId { get; set; }
    public bool IsEnded { get; set; }
}

public sealed class AutomationGoalMilestone
{
    public int HostId { get; set; }
    public string GoalId { get; set; } = string.Empty;
    public long Amount { get; set; }
}

public sealed class AutomationSourceAdmission
{
    public int HostId { get; set; }
    public DateTime AcceptEventsAfterUtc { get; set; }
}
