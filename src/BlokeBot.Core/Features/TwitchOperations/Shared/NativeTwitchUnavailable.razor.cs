using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.TwitchOperations.Shared;

public partial class NativeTwitchUnavailable
{
    [Parameter, EditorRequired]
    public required string FeatureName { get; set; }
}
