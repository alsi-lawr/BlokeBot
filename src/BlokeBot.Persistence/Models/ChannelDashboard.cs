namespace BlokeBot.Persistence.Models;

public sealed class ChannelDashboard
{
    public Guid Id { get; set; }
    public int HostId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DocumentJson { get; set; } = string.Empty;
    public long Revision { get; set; }
}
