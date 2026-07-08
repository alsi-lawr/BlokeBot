using Microsoft.AspNetCore.Components;

namespace BlokeBot.Components;

public abstract class BackgroundLoadComponent<TValue> : ComponentBase, IDisposable
{
    private CancellationTokenSource? activeLoad;
    private object? currentLoadKey;
    private bool disposed;
    private long version;

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

        if (Equals(currentLoadKey, key))
            return;

        StartBackgroundLoad(key);
    }

    public void Dispose()
    {
        disposed = true;
        activeLoad?.Cancel();
    }

    private void StartBackgroundLoad(object key)
    {
        activeLoad?.Cancel();

        var cts = new CancellationTokenSource();
        activeLoad = cts;
        currentLoadKey = key;
        BackgroundValue = default;
        BackgroundError = null;
        IsBackgroundLoading = true;
        var loadVersion = unchecked(++version);

        _ = RunBackgroundLoadAsync(loadVersion, cts);
    }

    private void ClearBackgroundLoad()
    {
        activeLoad?.Cancel();
        currentLoadKey = null;
        BackgroundValue = default;
        BackgroundError = null;
        IsBackgroundLoading = false;
        unchecked
        {
            version++;
        }
    }

    private async Task RunBackgroundLoadAsync(long loadVersion, CancellationTokenSource cts)
    {
        try
        {
            var value = await Task
                .Run(() => LoadBackgroundValueAsync(cts.Token), cts.Token)
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
            if (ReferenceEquals(activeLoad, cts))
                activeLoad = null;

            cts.Dispose();
        }
    }

    private async Task ApplyBackgroundLoadAsync(
        long loadVersion,
        CancellationTokenSource cts,
        Action apply
    )
    {
        if (disposed || cts.IsCancellationRequested || loadVersion != version)
            return;

        try
        {
            await InvokeAsync(() =>
            {
                if (disposed || cts.IsCancellationRequested || loadVersion != version)
                    return;

                apply();
                StateHasChanged();
            });
        }
        catch (InvalidOperationException) when (disposed) { }
        catch (ObjectDisposedException) when (disposed) { }
    }
}
