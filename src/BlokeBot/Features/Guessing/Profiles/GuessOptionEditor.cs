namespace BlokeBot.Features.Guessing.Profiles;

public sealed class GuessOptionEditor
{
    public string Name { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public string ReplyTarget { get; set; } = "chat";
}
