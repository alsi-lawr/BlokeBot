using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

public enum PluginMarketplaceCommandRejectionCode
{
    Unauthorized,
    CatalogUnavailable,
    CatalogEntryNotFound,
    PackageDownloadFailed,
    PackageInvalid,
    PackageStagingFailed,
    LifecycleRejected,
}

public abstract record PluginMarketplaceCommandOutcome
{
    private PluginMarketplaceCommandOutcome() { }

    public sealed record Completed(
        PluginLifecycleCommandOutcome Lifecycle,
        PluginMarketplaceReceipt? Receipt
    ) : PluginMarketplaceCommandOutcome;

    public sealed record Rejected(
        PluginMarketplaceCommandRejectionCode Code,
        PluginMarketplaceReceipt? Receipt
    ) : PluginMarketplaceCommandOutcome
    {
        public PluginLifecycleCommandRejectionCode? LifecycleRejection { get; init; }
    }
}

public sealed class PluginMarketplaceApplicationService
{
    private readonly PluginMarketplaceCatalogService _catalog;
    private readonly PluginMarketplacePackageStore _packages;
    private readonly IPluginLifecycleCoordinator _lifecycle;
    private readonly IPluginMarketplaceReceiptStore _receipts;
    private readonly TimeProvider _timeProvider;

    internal PluginMarketplaceApplicationService(
        PluginMarketplaceCatalogService catalog,
        PluginMarketplacePackageStore packages,
        IPluginLifecycleCoordinator lifecycle,
        IPluginMarketplaceReceiptStore receipts,
        TimeProvider timeProvider
    )
    {
        _catalog = catalog;
        _packages = packages;
        _lifecycle = lifecycle;
        _receipts = receipts;
        _timeProvider = timeProvider;
    }

    public ValueTask<PluginMarketplaceCommandOutcome> InstallAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    ) =>
        ActivateAsync(
            session,
            pluginId,
            release,
            PluginMarketplaceOperationKind.Install,
            cancellationToken
        );

    public ValueTask<PluginMarketplaceCommandOutcome> UpdateAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        CancellationToken cancellationToken
    ) =>
        ActivateAsync(
            session,
            pluginId,
            release,
            PluginMarketplaceOperationKind.Update,
            cancellationToken
        );

    public ValueTask<PluginMarketplaceCommandOutcome> RemoveAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) =>
        RunLifecycleAsync(
            session,
            pluginId,
            PluginMarketplaceOperationKind.Remove,
            _lifecycle.RemoveAsync,
            cancellationToken
        );

    public ValueTask<PluginMarketplaceCommandOutcome> RestartAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) =>
        RunLifecycleAsync(
            session,
            pluginId,
            PluginMarketplaceOperationKind.Restart,
            _lifecycle.RestartAsync,
            cancellationToken
        );

    public async ValueTask<PluginMarketplaceReceipt?> LoadLatestReceiptAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        CancellationToken cancellationToken
    ) => session.IsBotAdmin ? await _receipts.LoadAsync(pluginId, cancellationToken) : null;

    private async ValueTask<PluginMarketplaceCommandOutcome> ActivateAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginReleaseIdentity release,
        PluginMarketplaceOperationKind operation,
        CancellationToken cancellationToken
    )
    {
        if (!session.IsBotAdmin)
        {
            return new PluginMarketplaceCommandOutcome.Rejected(
                PluginMarketplaceCommandRejectionCode.Unauthorized,
                null
            );
        }

        var search = _catalog.Search(session, query: null);
        if (search is PluginMarketplaceSearchOutcome.Unavailable)
        {
            return new PluginMarketplaceCommandOutcome.Rejected(
                PluginMarketplaceCommandRejectionCode.CatalogUnavailable,
                null
            );
        }

        var entry = _catalog.Find(pluginId, release);
        if (entry is null)
        {
            var receipt = await SaveAsync(
                pluginId,
                operation,
                release,
                "catalog-entry-not-found",
                null,
                cancellationToken
            );
            return new PluginMarketplaceCommandOutcome.Rejected(
                PluginMarketplaceCommandRejectionCode.CatalogEntryNotFound,
                receipt
            );
        }

        var packageOperationId = PluginPackageOperationId.New();
        var preparation = await _packages.PrepareAsync(
            entry,
            packageOperationId,
            cancellationToken
        );
        if (preparation is PluginMarketplacePackagePreparationOutcome.Rejected rejected)
        {
            var (code, outcomeCode) = rejected.Code switch
            {
                PluginMarketplacePackageFailureCode.DownloadFailed => (
                    PluginMarketplaceCommandRejectionCode.PackageDownloadFailed,
                    "package-download-failed"
                ),
                PluginMarketplacePackageFailureCode.StagingFailed => (
                    PluginMarketplaceCommandRejectionCode.PackageStagingFailed,
                    "package-staging-failed"
                ),
                _ => (PluginMarketplaceCommandRejectionCode.PackageInvalid, "package-invalid"),
            };
            var receipt = await SaveAsync(
                pluginId,
                operation,
                release,
                outcomeCode,
                null,
                cancellationToken
            );
            return new PluginMarketplaceCommandOutcome.Rejected(code, receipt);
        }

        var package = ((PluginMarketplacePackagePreparationOutcome.Prepared)preparation).Package;
        var operationId = PluginLifecycleOperationId.New();
        var lifecycle =
            operation == PluginMarketplaceOperationKind.Update
                ? await _lifecycle.ReplaceAsync(operationId, package, cancellationToken)
                : await _lifecycle.ActivateAsync(operationId, package, cancellationToken);
        if (lifecycle is PluginLifecycleCommandOutcome.Succeeded)
        {
            await _packages.RetainOnlyAsync(
                package.Installation,
                package.PackageOperationId,
                cancellationToken
            );
        }
        else if (
            operation == PluginMarketplaceOperationKind.Update
            && lifecycle is PluginLifecycleCommandOutcome.Failed
        )
        {
            await _packages.RetainOnlyAsync(
                package.Installation,
                package.PackageOperationId,
                cancellationToken
            );
        }
        else if (lifecycle is PluginLifecycleCommandOutcome.Rejected)
        {
            await _packages.RemoveOperationAsync(
                package.Installation,
                package.PackageOperationId,
                cancellationToken
            );
        }
        return await RecordLifecycleAsync(
            pluginId,
            operation,
            release,
            lifecycle,
            cancellationToken
        );
    }

    private async ValueTask<PluginMarketplaceCommandOutcome> RunLifecycleAsync(
        AuthenticatedSession session,
        PluginId pluginId,
        PluginMarketplaceOperationKind operation,
        Func<
            PluginId,
            PluginLifecycleOperationId,
            CancellationToken,
            ValueTask<PluginLifecycleCommandOutcome>
        > run,
        CancellationToken cancellationToken
    )
    {
        if (!session.IsBotAdmin)
        {
            return new PluginMarketplaceCommandOutcome.Rejected(
                PluginMarketplaceCommandRejectionCode.Unauthorized,
                null
            );
        }

        var previous = await _receipts.LoadAsync(pluginId, cancellationToken);
        var lifecycle = await run(pluginId, PluginLifecycleOperationId.New(), cancellationToken);
        var release = lifecycle switch
        {
            PluginLifecycleCommandOutcome.Succeeded succeeded => succeeded
                .View
                .Installation
                .Release,
            PluginLifecycleCommandOutcome.Failed failed => failed.View.Installation.Release,
            PluginLifecycleCommandOutcome.Rejected { Current: { } current } => current
                .Installation
                .Release,
            _ => previous?.Release,
        };
        return await RecordLifecycleAsync(
            pluginId,
            operation,
            release,
            lifecycle,
            cancellationToken
        );
    }

    private async ValueTask<PluginMarketplaceCommandOutcome> RecordLifecycleAsync(
        PluginId pluginId,
        PluginMarketplaceOperationKind operation,
        PluginReleaseIdentity? release,
        PluginLifecycleCommandOutcome lifecycle,
        CancellationToken cancellationToken
    )
    {
        var (code, detail) = lifecycle switch
        {
            PluginLifecycleCommandOutcome.Succeeded succeeded => (
                succeeded.View.LatestOutcome.Code.ToString(),
                succeeded.View.LatestOutcome.Detail?.Value
            ),
            PluginLifecycleCommandOutcome.Failed failed => (
                failed.View.LatestOutcome.FailureCode?.ToString() ?? "Failed",
                failed.View.LatestOutcome.Detail?.Value
            ),
            PluginLifecycleCommandOutcome.Removed => ("Removed", null),
            PluginLifecycleCommandOutcome.Rejected rejected => ($"Rejected{rejected.Code}", null),
            _ => throw new InvalidOperationException("Unknown plugin lifecycle outcome."),
        };
        if (lifecycle is PluginLifecycleCommandOutcome.Removed)
        {
            return new PluginMarketplaceCommandOutcome.Completed(lifecycle, null);
        }

        var receipt = await SaveAsync(
            pluginId,
            operation,
            release,
            code,
            detail,
            cancellationToken
        );
        return lifecycle is PluginLifecycleCommandOutcome.Rejected lifecycleRejected
            ? new PluginMarketplaceCommandOutcome.Rejected(
                PluginMarketplaceCommandRejectionCode.LifecycleRejected,
                receipt
            )
            {
                LifecycleRejection = lifecycleRejected.Code,
            }
            : new PluginMarketplaceCommandOutcome.Completed(lifecycle, receipt);
    }

    private async ValueTask<PluginMarketplaceReceipt> SaveAsync(
        PluginId pluginId,
        PluginMarketplaceOperationKind operation,
        PluginReleaseIdentity? release,
        string code,
        string? detail,
        CancellationToken cancellationToken
    )
    {
        var receipt = new PluginMarketplaceReceipt(
            pluginId,
            operation,
            release,
            code,
            detail,
            _timeProvider.GetUtcNow()
        );
        await _receipts.SaveAsync(receipt, cancellationToken);
        return receipt;
    }
}
