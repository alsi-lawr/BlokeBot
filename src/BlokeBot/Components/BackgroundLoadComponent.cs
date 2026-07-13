using Microsoft.AspNetCore.Components;

namespace BlokeBot.Components;

public abstract class BackgroundLoadComponent<TValue> : ComponentBase, IDisposable
{
    private CancellationTokenSource? _activeLoad;
    private object? _currentLoadKey;
    private bool _disposed;
    private long _version;

    protected TValue? BackgroundValue { get; private set; }
    protected Exception? BackgroundError { get; private set; }
    protected bool IsBackgroundLoading { get; private set; }

    protected abstract object? BackgroundLoadKey { get; }

    protected abstract Task<TValue> LoadBackgroundValueAsync(CancellationToken ct);

    protected override void OnParametersSet()
    {
        var key = BackgroundLoadKey;
        if (key is null)
        {
            ClearBackgroundLoad();
            return;
        }

        if (Equals(_currentLoadKey, key))
        {
            return;
        }

        StartBackgroundLoad(key);
    }

    public void Dispose()
    {
        _disposed = true;
        _activeLoad?.Cancel();
    }

    private void StartBackgroundLoad(object key)
    {
        _activeLoad?.Cancel();

        var cts = new CancellationTokenSource();
        _activeLoad = cts;
        _currentLoadKey = key;
        BackgroundValue = default;
        BackgroundError = null;
        IsBackgroundLoading = true;
        var loadVersion = unchecked(++_version);

        _ = RunBackgroundLoadAsync(loadVersion, cts);
    }

    private void ClearBackgroundLoad()
    {
        _activeLoad?.Cancel();
        _currentLoadKey = null;
        BackgroundValue = default;
        BackgroundError = null;
        IsBackgroundLoading = false;
        unchecked
        {
            _version++;
        }
    }

    private async Task RunBackgroundLoadAsync(long loadVersion, CancellationTokenSource cts)
    {
        try
        {
            var value = await Task.Run(() => LoadBackgroundValueAsync(cts.Token), cts.Token)
                .ConfigureAwait(false);
            await ApplyBackgroundLoadAsync(
                    loadVersion,
                    cts,
                    () =>
                    {
                        BackgroundValue = value;
                        BackgroundError = null;
                        IsBackgroundLoading = false;
                    }
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await ApplyBackgroundLoadAsync(
                    loadVersion,
                    cts,
                    () =>
                    {
                        BackgroundValue = default;
                        BackgroundError = ex;
                        IsBackgroundLoading = false;
                    }
                )
                .ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(_activeLoad, cts))
            {
                _activeLoad = null;
            }

            cts.Dispose();
        }
    }

    private async Task ApplyBackgroundLoadAsync(
        long loadVersion,
        CancellationTokenSource cts,
        Action apply
    )
    {
        if (_disposed || cts.IsCancellationRequested || loadVersion != _version)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed || cts.IsCancellationRequested || loadVersion != _version)
                {
                    return;
                }

                apply();
                StateHasChanged();
            });
        }
        catch (InvalidOperationException) when (_disposed) { }
        catch (ObjectDisposedException) when (_disposed) { }
    }
}
