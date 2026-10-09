namespace BlokeBot.Persistence.Models;

public sealed class FullOverlayEventFeedBinding
{
    public long Id { get; set; }
    public long FullOverlayId { get; set; }
    public int HostId { get; set; }
    public Guid BindingId { get; set; }
    public long PublishedVersion { get; set; }
    public bool IsEnabled { get; set; }
    public string ConfigurationJson { get; set; } = string.Empty;
    public FullOverlay FullOverlay { get; set; } = null!;
}
