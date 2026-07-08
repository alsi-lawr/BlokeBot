namespace BlokeBot.Persistence.Models;

public sealed class HostModAccessSettings
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public bool ModsEnabled { get; set; } = true;
}
