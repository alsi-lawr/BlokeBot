using BlokeBot.Features.Guessing.Replies;

namespace BlokeBot.Features.Guessing.Profiles;

public sealed class GuessRoundProfileEditor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public ReplySettingsEditor Replies { get; set; } = new();
    public List<GuessOptionEditor> Options { get; set; } = [];
}
