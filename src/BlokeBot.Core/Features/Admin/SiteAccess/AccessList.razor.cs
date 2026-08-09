using BlokeBot.Core.Features.AccessLists;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Admin.SiteAccess;

public partial class AccessList
{
    private const int _removalAnimationDelayMs = 150;
    private readonly HashSet<string> _pendingRemovals = new(StringComparer.OrdinalIgnoreCase);

    [Parameter]
    public Func<Task> Add { get; set; } = static () => Task.CompletedTask;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public string? DisabledText { get; set; }

    [Parameter]
    public string EmptyText { get; set; } = string.Empty;

    [Parameter]
    public IReadOnlyList<AccessListEntryProfile> Entries { get; set; } = [];

    [Parameter]
    public string NewLogin { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> NewLoginChanged { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = string.Empty;

    [Parameter]
    public Func<string, Task> Remove { get; set; } = static _ => Task.CompletedTask;

    [Parameter]
    public string Title { get; set; } = string.Empty;

    private async Task OnInput(ChangeEventArgs args)
    {
        NewLogin = args.Value?.ToString() ?? string.Empty;
        await NewLoginChanged.InvokeAsync(NewLogin);
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
