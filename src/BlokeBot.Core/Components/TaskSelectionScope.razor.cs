using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components;

public partial class TaskSelectionScope
{
    private readonly List<CollapsibleSection> _sections = [];
    private readonly HashSet<string> _taskKeys = [];
    private bool _hasExplicitSelection;
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

    internal void Register(CollapsibleSection section, string taskKey)
    {
        _sections.Add(section);
        _taskKeys.Add(taskKey);
    }

    internal async Task SelectAsync(string taskKey)
    {
        _hasExplicitSelection = true;
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
            if (!_hasExplicitSelection && _taskKeys.Contains(rememberedTask ?? string.Empty))
            {
                _selectedTask = rememberedTask;
                UpdateSections();
            }
            else if (_hasExplicitSelection)
            {
                await _module.InvokeVoidAsync(
                    "writeString",
                    _storageKey,
                    _selectedTask ?? DefaultTask
                );
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
