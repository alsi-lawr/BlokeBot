using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    private ElementReference _viewportDialog;
    private string _viewportWidth = "1920";
    private string _viewportHeight = "1080";
    private bool _pan;

    private Task OpenViewportAsync() =>
        _module is null
            ? Task.CompletedTask
            : _module.InvokeVoidAsync("openDialog", _viewportDialog).AsTask();

    private Task CloseViewportAsync() =>
        _module is null
            ? Task.CompletedTask
            : _module.InvokeVoidAsync("closeDialog", _viewportDialog).AsTask();

    private async Task ApplyViewportAsync()
    {
        if (
            !double.TryParse(
                _viewportWidth,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var width
            )
            || !double.IsFinite(width)
            || width <= 0
            || !double.TryParse(
                _viewportHeight,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var height
            )
            || !double.IsFinite(height)
            || height <= 0
        )
        {
            _message = "Preview width and height must be positive finite numbers.";
            return;
        }
        if (_client is not null)
        {
            await _client.InvokeVoidAsync("viewport", width, height);
        }
        await CloseViewportAsync();
    }

    private async Task PanAsync()
    {
        _pan = !_pan;
        if (_client is not null)
        {
            await _client.InvokeVoidAsync("pan", _pan);
        }
    }
}
