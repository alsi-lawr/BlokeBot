using BlokeBot.Core.Features.Guessing.Commands;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public partial class CommandAliasSettingsSection
{
    [Parameter, EditorRequired]
    public CommandAliasEditor Aliases { get; set; } = new();
}
