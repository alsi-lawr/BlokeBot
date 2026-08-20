using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigBotStatusStage
{
    [Parameter, EditorRequired]
    public required HostConfigState State { get; set; }

    [Parameter]
    public bool CanAuthorizeSelectedHost { get; set; }

    [Parameter, EditorRequired]
    public required string Summary { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public long FocusRequest { get; set; }

    [Parameter, EditorRequired]
    public required string AuthorizationBadgeClass { get; set; }

    [Parameter, EditorRequired]
    public required string AuthorizationDotClass { get; set; }

    [Parameter, EditorRequired]
    public required string AuthorizationText { get; set; }

    [Parameter, EditorRequired]
    public required string OperationsAuthorizationBadgeClass { get; set; }

    [Parameter, EditorRequired]
    public required string OperationsAuthorizationDotClass { get; set; }

    [Parameter, EditorRequired]
    public required string OperationsAuthorizationText { get; set; }

    [Parameter]
    public string? BotAccountStatusReloadKey { get; set; }

    [Parameter, EditorRequired]
    public required string ActiveBotAccountName { get; set; }

    [Parameter, EditorRequired]
    public required string ActiveBotReconnectUrl { get; set; }

    [Parameter, EditorRequired]
    public required string RuntimeBadgeClass { get; set; }

    [Parameter, EditorRequired]
    public required string RuntimeDotClass { get; set; }

    [Parameter, EditorRequired]
    public required string RuntimeText { get; set; }

    [Parameter, EditorRequired]
    public required string StartRuntimeTooltip { get; set; }

    [Parameter, EditorRequired]
    public required string StopRuntimeTooltip { get; set; }

    [Parameter]
    public bool CanStart { get; set; }

    [Parameter]
    public bool CanStop { get; set; }

    [Parameter]
    public EventCallback Reload { get; set; }

    [Parameter]
    public EventCallback ClearChannelAuthorization { get; set; }

    [Parameter]
    public EventCallback DisconnectTwitchIntegration { get; set; }

    [Parameter]
    public EventCallback Start { get; set; }

    [Parameter]
    public EventCallback Stop { get; set; }
}
