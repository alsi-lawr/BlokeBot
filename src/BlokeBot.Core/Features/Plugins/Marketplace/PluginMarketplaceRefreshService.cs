namespace BlokeBot.Core.Features.Plugins;

internal sealed class PluginMarketplaceRefreshService(
    PluginMarketplaceCatalogRegistry catalog,
    PluginMarketplacePackageStore packages,
    PluginMarketplaceStorageOptions options,
    TimeProvider timeProvider,
    ILogger<PluginMarketplaceRefreshService> logger
) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _run;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await packages.CleanupInterruptedAsync(cancellationToken);
            await catalog.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Plugin marketplace local state could not be initialized."
            );
        }

        _run = RunAsync(_stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_run is not null)
        {
            await _run.WaitAsync(cancellationToken);
        }
    }

    public void Dispose() => _stopping.Dispose();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
            using var timer = new PeriodicTimer(options.RefreshInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
    }

    private async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await catalog.RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Plugin marketplace catalog refresh could not be saved.");
        }
    }
}
