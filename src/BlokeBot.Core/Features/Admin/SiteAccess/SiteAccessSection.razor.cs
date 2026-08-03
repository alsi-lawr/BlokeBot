using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.SiteAccess;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Admin.SiteAccess;

public partial class SiteAccessSection
{
    [Parameter, EditorRequired]
    public Func<Task> AddBlacklist { get; set; } = static () => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<Task> AddWhitelist { get; set; } = static () => Task.CompletedTask;

    [Parameter]
    public IReadOnlyList<AccessListEntryProfile> BlacklistEntries { get; set; } = [];

    [Parameter]
    public string NewBlacklistLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewBlacklistLoginChanged { get; set; }

    [Parameter]
    public string NewWhitelistLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewWhitelistLoginChanged { get; set; }

    [Parameter, EditorRequired]
    public Func<string, Task> RemoveBlacklist { get; set; } = static _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public Func<string, Task> RemoveWhitelist { get; set; } = static _ => Task.CompletedTask;

    [Parameter, EditorRequired]
    public SiteAccessAdminState State { get; set; } = new(false, [], []);

    [Parameter, EditorRequired]
    public Func<ChangeEventArgs, Task> ToggleWhitelist { get; set; } =
        static _ => Task.CompletedTask;

    [Parameter]
    public IReadOnlyList<AccessListEntryProfile> WhitelistEntries { get; set; } = [];

    private async Task OnBlacklistInput(string value)
    {
        NewBlacklistLogin = value;
        await NewBlacklistLoginChanged.InvokeAsync(value);
    }

    private async Task OnWhitelistInput(string value)
    {
        NewWhitelistLogin = value;
        await NewWhitelistLoginChanged.InvokeAsync(value);
    }
}
