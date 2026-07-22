namespace BlokeBot.Persistence.Models;

public sealed class CustomCommandInvocationResetAudit
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public int? CustomCommandId { get; set; }

    public string CommandName { get; set; } = string.Empty;

    public string ActorTwitchUserId { get; set; } = string.Empty;

    public string ActorLogin { get; set; } = string.Empty;

    public CustomCommandInvocationResetScope Scope { get; set; }

    public string? TargetTwitchUserId { get; set; }

    public string? TargetLogin { get; set; }

    public int AffectedClaimCount { get; set; }

    public DateTime ResetAtUtc { get; set; }

    public CustomCommand? Command { get; set; }
}
