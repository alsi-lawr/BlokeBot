namespace BlokeBot.Persistence.Models;

public sealed class AutomationScenario
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FixtureJson { get; set; } = string.Empty;
    public AutomationFlow Flow { get; set; } = null!;
}
