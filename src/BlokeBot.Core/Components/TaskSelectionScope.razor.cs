using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components;

public partial class TaskSelectionScope
{
    private readonly List<CollapsibleSection> _sections = [];
    private IJSObjectReference? _module;
    private string? _selectedTask;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter, EditorRequired]
    public string DefaultTask { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string PreferenceKey { get; set; } = string.Empty;

    internal bool IsSelected(string taskKey)
    {
        return (_selectedTask ?? DefaultTask) == taskKey;
    }

    internal void Register(CollapsibleSection section)
    {
        _sections.Add(section);
    }

    internal async Task SelectAsync(string taskKey)
    {
        _selectedTask = taskKey;
        UpdateSections();
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("writeString", _storageKey, taskKey);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Components/CollapsibleSection.razor.js"
            );
            var rememberedTask = await _module.InvokeAsync<string?>("readString", _storageKey);
            if (!string.IsNullOrWhiteSpace(rememberedTask))
            {
                _selectedTask = rememberedTask;
                UpdateSections();
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private string _storageKey => $"blokebot.task.{PreferenceKey}";

    private void UpdateSections()
    {
        foreach (var section in _sections)
        {
            section.UpdateSelection();
        }
    }
}
