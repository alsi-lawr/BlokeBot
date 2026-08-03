using BlokeBot.Core.Features.AccessLists;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostAccessList
{
    private const int _removalAnimationDelayMs = 150;
    private readonly HashSet<string> _pendingRemovals = new(StringComparer.OrdinalIgnoreCase);

    [Parameter]
    public Func<Task> Add { get; set; } = () => Task.CompletedTask;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public IReadOnlyList<AccessListEntryProfile> Entries { get; set; } = [];

    [Parameter]
    public string NewLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewLoginChanged { get; set; }

    [Parameter]
    public Func<string, Task> Remove { get; set; } = _ => Task.CompletedTask;

    [Parameter]
    public string Title { get; set; } = string.Empty;

    private string _containerClass =>
        Disabled ? "surface-muted rounded-lg p-4 opacity-50" : "surface-muted rounded-lg p-4";

    private async Task OnInput(ChangeEventArgs args)
    {
        NewLogin = args.Value?.ToString() ?? string.Empty;
        await NewLoginChanged.InvokeAsync(NewLogin);
    }

    private string EntryRowClass(string entry)
    {
        const string BaseClass =
            "motion-list__item surface-row flex items-center justify-between rounded-md px-3 py-2";
        return _pendingRemovals.Contains(entry)
            ? $"{BaseClass} motion-list__item--removing"
            : BaseClass;
    }

    private async Task RemoveEntryAsync(string entry)
    {
        if (!_pendingRemovals.Add(entry))
        {
            return;
        }

        StateHasChanged();
        try
        {
            await Task.Delay(_removalAnimationDelayMs);
            await Remove(entry);
        }
        finally
        {
            _ = _pendingRemovals.Remove(entry);
        }
    }
}
