using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components;

public partial class EditorWorkspace
{
    [Parameter]
    public string Class { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public RenderFragment<RenderFragment>? Header { get; set; }

    [Parameter]
    public EventCallback<bool> FocusChanged { get; set; }

    [Inject]
    private ILogger<EditorWorkspace> _logger { get; set; } = default!;

    [Parameter]
    public EventCallback<JSException> InteropFailure { get; set; }

    internal enum FullscreenResult
    {
        Completed,
        EnterRefused,
        ExitRefused,
    }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? Attributes { get; set; }
    public ElementReference Element { get; private set; }
    public bool FocusMode { get; private set; }
    private IJSObjectReference? _module;
    private IJSObjectReference? _browser;
    private DotNetObjectReference<EditorWorkspace>? _reference;
    private bool _fullscreen;
    private bool _disposed;
    private string? _failure;
    private Task? _initialization;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }
        _initialization = InitializeAsync();
        await _initialization;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Components/EditorWorkspace.razor.js"
            );
            if (_disposed)
            {
                return;
            }
            _reference = DotNetObjectReference.Create(this);
            _browser = await _module.InvokeAsync<IJSObjectReference>(
                "createWorkspace",
                Element,
                _reference
            );
        }
        catch (JSDisconnectedException) { }
        catch (JSException exception)
        {
            await InteropFailedAsync(exception);
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ToggleFocusAsync()
    {
        FocusMode = !FocusMode;
        await FocusChanged.InvokeAsync(FocusMode);
    }

    private async Task FullscreenAsync()
    {
        _failure = null;
        if (_browser is null)
        {
            _failure = "Browser full screen controls are unavailable. Try again.";
            return;
        }
        try
        {
            var result = await _browser.InvokeAsync<FullscreenResult>("toggleFullscreen");
            _failure = result switch
            {
                FullscreenResult.Completed => null,
                FullscreenResult.EnterRefused => "Browser full screen did not start. Try again.",
                FullscreenResult.ExitRefused => "Browser full screen did not exit. Try again.",
            };
        }
        catch (JSException exception)
        {
            await InteropFailedAsync(exception);
        }
    }

    private async Task InteropFailedAsync(JSException exception)
    {
        _failure = "Browser full screen controls are unavailable. Try again.";
        if (InteropFailure.HasDelegate)
        {
            await InteropFailure.InvokeAsync(exception);
        }
        else
        {
            _logger.LogWarning(exception, "Editor workspace browser interop failed");
        }
    }

    [JSInvokable]
    public Task BrowserFullscreenChangedAsync(bool active) =>
        InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }
            _fullscreen = active;
            StateHasChanged();
        });

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_initialization is not null)
        {
            await _initialization;
        }
        try
        {
            if (_browser is not null)
            {
                await _browser.InvokeVoidAsync("dispose");
                await _browser.DisposeAsync();
            }
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        _reference?.Dispose();
        GC.SuppressFinalize(this);
    }
}
