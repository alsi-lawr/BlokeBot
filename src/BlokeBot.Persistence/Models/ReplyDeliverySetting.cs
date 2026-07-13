namespace BlokeBot.Persistence.Models;

public sealed class ReplyDeliverySetting
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public required ReplyFeature Feature { get; set; }
    public int ScopeId { get; set; }
    public string ReplyKey { get; set; } = string.Empty;
    public required ReplyDeliveryTarget Target { get; set; }
}
