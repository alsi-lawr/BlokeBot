using BlokeBot.Core.Components.Studio;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigAccessStages
{
    [Parameter, EditorRequired]
    public required HostConfigState State { get; set; }

    [Parameter, EditorRequired]
    public required string BotAccountSummary { get; set; }

    [Parameter]
    public bool BotAccountOpen { get; set; }

    [Parameter]
    public EventCallback<bool> BotAccountOpenChanged { get; set; }

    [Parameter]
    public EventCallback ToggleBotOverride { get; set; }

    [Parameter]
    public required Func<Task> ClearBotOverrideAuthorization { get; set; }

    [Parameter]
    public required Func<Task> Reload { get; set; }

    [Parameter]
    public EventCallback ToggleWhisperResponses { get; set; }

    [Parameter, EditorRequired]
    public required string WhisperQuotaBadgeClass { get; set; }

    [Parameter, EditorRequired]
    public required string WhisperQuotaDotClass { get; set; }

    [Parameter, EditorRequired]
    public required string WhisperQuotaText { get; set; }

    [Parameter, EditorRequired]
    public required string ModeratorHelpSummary { get; set; }

    [Parameter]
    public bool ModeratorHelpOpen { get; set; }

    [Parameter]
    public EventCallback<bool> ModeratorHelpOpenChanged { get; set; }

    [Parameter]
    public long ModeratorHelpFocusRequest { get; set; }

    [Parameter]
    public EventCallback ToggleModeratorHelp { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<StudioSegmentedOption<bool>> AccessModeOptions { get; set; }

    [Parameter]
    public EventCallback<bool> SetAllowModsByDefault { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<AccessListEntryProfile> WhitelistEntries { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<AccessListEntryProfile> BlacklistEntries { get; set; }

    [Parameter, EditorRequired]
    public required string NewWhitelistLogin { get; set; }

    [Parameter]
    public EventCallback<string> NewWhitelistLoginChanged { get; set; }

    [Parameter, EditorRequired]
    public required string NewBlacklistLogin { get; set; }

    [Parameter]
    public EventCallback<string> NewBlacklistLoginChanged { get; set; }

    [Parameter]
    public required Func<AccessListEntryKind, Task> AddAccess { get; set; }

    [Parameter]
    public required Func<(AccessListEntryKind Kind, string Login), Task> RemoveAccess { get; set; }
}
