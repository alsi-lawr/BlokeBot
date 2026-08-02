using BlokeBot.Core.Features.Guessing.Replies;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public partial class ReplySettingsSection
{
    [Parameter, EditorRequired]
    public ReplySettingsEditor Replies { get; set; } = new();
}
