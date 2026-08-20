using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigChatToolsStage
{
    [Parameter, EditorRequired]
    public required IReadOnlyList<HostFeatureCardState> Features { get; set; }

    [Parameter, EditorRequired]
    public required string Summary { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public long FocusRequest { get; set; }

    [Parameter, EditorRequired]
    public required Func<HostFeatureCardState, string> CardClass { get; set; }

    [Parameter, EditorRequired]
    public required Func<HostFeatureCardState, string> IconClass { get; set; }

    [Parameter, EditorRequired]
    public required Func<HostFeatureFlags, MarkupString> Icon { get; set; }

    [Parameter, EditorRequired]
    public required Func<HostFeatureCardState, string> BadgeClass { get; set; }

    [Parameter, EditorRequired]
    public required Func<HostFeatureCardState, string> DotClass { get; set; }

    [Parameter]
    public EventCallback<(HostFeatureFlags Feature, bool Enabled)> SetFeatureEnabled { get; set; }
}
