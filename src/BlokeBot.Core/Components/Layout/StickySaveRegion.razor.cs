using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components.Layout;

public partial class StickySaveRegion
{
    private ElementReference _element;
    private Task? _initialization;
    private IJSObjectReference? _module;
    private IJSObjectReference? _registration;
    private Task? _disposal;
    private bool _disposed;

    [Parameter]
    public bool Active { get; set; } = true;

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Parameter, EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string Class { get; set; } = string.Empty;

    [Parameter]
    public PageSaveFeedback? Feedback { get; set; }

    [Parameter]
    public StickySaveScope Scope { get; set; } = StickySaveScope.Page;

    private string _class =>
        string.IsNullOrWhiteSpace(Class) ? "sticky-save-region" : $"sticky-save-region {Class}";

    private string _feedbackRole =>
        Feedback?.Kind is PageSaveFeedbackKind.Validation or PageSaveFeedbackKind.Failure
            ? "alert"
            : "status";

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed)
        {
            return Task.CompletedTask;
        }

        _initialization = InitializeAsync();
        return _initialization;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var module = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./Components/Layout/StickySaveRegion.razor.js"
            );
            if (_disposed)
            {
                await DisposeReferenceAsync(module);
                return;
            }

            _module = module;
            var registration = await module.InvokeAsync<IJSObjectReference>("register", _element);
            if (_disposed)
            {
                await DisposeRegistrationAsync(registration);
                return;
            }

            _registration = registration;
        }
        catch (Exception exception) when (_disposed && IsExpectedInteropShutdown(exception)) { }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _disposal ??= DisposeCoreAsync();
        return new(_disposal);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            if (_initialization is not null)
            {
                await _initialization;
            }
        }
        catch (Exception exception) when (IsExpectedInteropShutdown(exception)) { }
        finally
        {
            var registration = _registration;
            _registration = null;
            try
            {
                if (registration is not null)
                {
                    await DisposeRegistrationAsync(registration);
                }
            }
            finally
            {
                var module = _module;
                _module = null;
                if (module is not null)
                {
                    await DisposeReferenceAsync(module);
                }

                GC.SuppressFinalize(this);
            }
        }
    }

    private static async ValueTask DisposeRegistrationAsync(IJSObjectReference registration)
    {
        try
        {
            try
            {
                await registration.InvokeVoidAsync("dispose");
            }
            catch (Exception exception) when (IsExpectedInteropShutdown(exception)) { }
        }
        finally
        {
            await DisposeReferenceAsync(registration);
        }
    }

    private static async ValueTask DisposeReferenceAsync(IJSObjectReference reference)
    {
        try
        {
            await reference.DisposeAsync();
        }
        catch (Exception exception) when (IsExpectedInteropShutdown(exception)) { }
    }

    private static bool IsExpectedInteropShutdown(Exception exception) =>
        exception is JSDisconnectedException or OperationCanceledException;
}
