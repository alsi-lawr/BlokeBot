namespace BlokeBot.Persistence.Models;

public sealed class ReplyDeliverySetting
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string Feature { get; set; } = string.Empty;
    public int ScopeId { get; set; }
    public string ReplyKey { get; set; } = string.Empty;
    public string Target { get; set; } = "chat";
}
