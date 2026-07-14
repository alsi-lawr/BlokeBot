using Microsoft.AspNetCore.Components;

namespace BlokeBot.Components;

public abstract class BackgroundLoadComponent<TValue, TLoadIdentity> : ComponentBase, IDisposable
    where TLoadIdentity : class, IEquatable<TLoadIdentity>
{
    private CancellationTokenSource? _activeLoad;
    private TLoadIdentity? _currentLoadIdentity;
    private bool _disposed;
    private long _version;

    protected TValue? BackgroundValue { get; private set; }
    protected Exception? BackgroundError { get; private set; }
    protected bool IsBackgroundLoading { get; private set; }

    protected abstract TLoadIdentity? BackgroundLoadIdentity { get; }

    protected abstract Task<TValue> LoadBackgroundValueAsync(CancellationToken ct);

    protected override void OnParametersSet()
    {
        var identity = BackgroundLoadIdentity;
        if (identity is null)
        {
            ClearBackgroundLoad();
            return;
        }

        if (EqualityComparer<TLoadIdentity>.Default.Equals(_currentLoadIdentity, identity))
        {
            return;
        }

        StartBackgroundLoad(identity);
    }

    public void Dispose()
    {
        _disposed = true;
        _activeLoad?.Cancel();
    }

    private void StartBackgroundLoad(TLoadIdentity identity)
    {
        _activeLoad?.Cancel();

        var cts = new CancellationTokenSource();
        _activeLoad = cts;
        _currentLoadIdentity = identity;
        BackgroundValue = default;
        BackgroundError = null;
        IsBackgroundLoading = true;
        var loadVersion = unchecked(++_version);

        _ = RunBackgroundLoadAsync(loadVersion, cts);
    }

    private void ClearBackgroundLoad()
    {
        _activeLoad?.Cancel();
        _currentLoadIdentity = null;
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
