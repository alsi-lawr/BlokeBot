using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components;

public partial class FileDropInput
{
    private ElementReference _dropTarget;
    private IJSObjectReference? _binding;
    private IJSObjectReference? _module;
    private SelectedFile? _selection;
    private bool _handling { get; set; }

    [Parameter, EditorRequired]
    public string Accept { get; set; } = string.Empty;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public bool Compact { get; set; }

    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public string Prompt { get; set; } = "Drag and drop here";

    [Parameter, EditorRequired]
    public EventCallback<InputFileChangeEventArgs> OnChange { get; set; }

    internal string? SelectedFileLabel =>
        _selection is null ? null : $"{_selection.Name} · {SizeLabel(_selection.Size)}";

    private bool _isDisabled => Disabled || _handling;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                _module = await _js.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./Components/FileDropInput.razor.js"
                );
                _binding = await _module.InvokeAsync<IJSObjectReference>(
                    "bindFileDrop",
                    _dropTarget,
                    _isDisabled
                );
                return;
            }

            if (_binding is not null)
            {
                await _binding.InvokeVoidAsync("setDisabled", _isDisabled);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_binding is not null)
        {
            try
            {
                await _binding.InvokeVoidAsync("dispose");
                await _binding.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }
    }

    internal async Task HandleSelectionAsync(InputFileChangeEventArgs args)
    {
        if (_isDisabled)
        {
            return;
        }

        var file = args.File;
        _selection = new SelectedFile(file.Name, file.Size);
        _handling = true;
        try
        {
            await OnChange.InvokeAsync(args);
        }
        finally
        {
            _handling = false;
        }
    }

    private async Task BrowseAsync()
    {
        if (_isDisabled || _binding is null)
        {
            return;
        }

        try
        {
            await _binding.InvokeVoidAsync("browse");
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    private static string SizeLabel(long bytes) =>
        bytes switch
        {
            1 => "1 byte",
            < 1024 => $"{bytes} bytes",
            < (1024 * 1024) => $"{bytes / 1024m:0.##} KB",
            _ => $"{bytes / (1024m * 1024m):0.##} MB",
        };

    private sealed record SelectedFile(string Name, long Size);
}
