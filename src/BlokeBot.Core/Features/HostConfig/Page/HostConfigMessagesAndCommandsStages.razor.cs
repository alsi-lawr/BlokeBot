using BlokeBot.Core.Features.Commands;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigMessagesAndCommandsStages
{
    [Parameter, EditorRequired]
    public required HostConfigState State { get; set; }

    [Parameter, EditorRequired]
    public required string StartupMessageSummary { get; set; }

    [Parameter]
    public bool StartupMessageOpen { get; set; }

    [Parameter]
    public EventCallback<bool> StartupMessageOpenChanged { get; set; }

    [Parameter]
    public bool StartupMessageEnabled { get; set; }

    [Parameter]
    public EventCallback ToggleStartupMessageEnabled { get; set; }

    [Parameter]
    public int MaxChatMessageLength { get; set; }

    [Parameter, EditorRequired]
    public required string StartupMessageText { get; set; }

    [Parameter]
    public EventCallback<ChangeEventArgs> StartupMessageTextChanged { get; set; }

    [Parameter]
    public bool StartupMessageSaving { get; set; }

    [Parameter]
    public EventCallback SaveStartupMessage { get; set; }

    [Parameter, EditorRequired]
    public required string CommandsSummary { get; set; }

    [Parameter]
    public bool CommandsOpen { get; set; }

    [Parameter]
    public EventCallback<bool> CommandsOpenChanged { get; set; }

    [Parameter, EditorRequired]
    public required string CommandsAliases { get; set; }

    [Parameter]
    public EventCallback<string> CommandsAliasesChanged { get; set; }

    [Parameter]
    public bool CommandsSaving { get; set; }

    [Parameter]
    public EventCallback SaveCommands { get; set; }

    [Parameter]
    public bool CommandInventoryOpen { get; set; }

    [Parameter]
    public EventCallback<bool> CommandInventoryOpenChanged { get; set; }

    [Parameter]
    public bool CommandCatalogLoading { get; set; }

    [Parameter]
    public ViewerCommandCatalogSnapshot? CommandCatalog { get; set; }

    [Parameter, EditorRequired]
    public required Func<ViewerCommandCatalogEntry, string?> AvailabilitySummary { get; set; }
}
