using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal static class PluginAdminCopy
{
    internal static string Fault(PluginLifecycleOutcome outcome) =>
        outcome.FailureCode switch
        {
            PluginLifecycleFailureCode.PreparationRejected =>
                "The package failed its preparation checks. Apply another catalogue release.",
            PluginLifecycleFailureCode.PreparationFailed =>
                "BlokeBot could not prepare the package. Apply the update again.",
            PluginLifecycleFailureCode.MigrationFailed =>
                "The data update failed. Check the fault detail, then restart the plugin.",
            PluginLifecycleFailureCode.ActivationFailed =>
                "The plugin could not become active. Check the fault detail, then restart it.",
            PluginLifecycleFailureCode.WorkerStartFailed =>
                "The plugin process could not start. Check the fault detail, then restart it.",
            PluginLifecycleFailureCode.WorkerDisposalFailed =>
                "The old plugin process did not stop correctly. Restart the plugin.",
            PluginLifecycleFailureCode.WorkerExited =>
                "The plugin process stopped. Restart the plugin.",
            PluginLifecycleFailureCode.DrainTimedOut =>
                "The plugin work did not stop in time. Restart the plugin.",
            PluginLifecycleFailureCode.CancellationFailed =>
                "BlokeBot could not cancel the plugin work. Restart the plugin.",
            PluginLifecycleFailureCode.RemovalFailed =>
                "The removal failed. Reload the status, then remove the plugin again.",
            PluginLifecycleFailureCode.RecoveryPackageUnavailable =>
                "The saved package is unavailable. Apply a catalogue update.",
            PluginLifecycleFailureCode.RecoveryFailed =>
                "BlokeBot could not recover the plugin. Apply an update or restart it.",
            PluginLifecycleFailureCode.GenerationExhausted =>
                "The plugin used all process generations. Restart BlokeBot before another operation.",
            null => "The plugin operation failed. Reload the status and try again.",
        };

    internal static string Rejection(PluginMarketplaceCommandOutcome.Rejected rejected) =>
        rejected.Code switch
        {
            PluginMarketplaceCommandRejectionCode.Unauthorized =>
                "Your BotAdmin access is unavailable. Sign in again.",
            PluginMarketplaceCommandRejectionCode.CatalogUnavailable =>
                "No saved catalogue is available. Try again after the next refresh.",
            PluginMarketplaceCommandRejectionCode.CatalogEntryNotFound =>
                "This version or tag is no longer in the catalogue. Reload the status.",
            PluginMarketplaceCommandRejectionCode.PackageDownloadFailed =>
                "The package download failed. Check the catalogue status and try again.",
            PluginMarketplaceCommandRejectionCode.PackageInvalid =>
                "The package failed validation. Choose another compatible release.",
            PluginMarketplaceCommandRejectionCode.PackageStagingFailed =>
                "BlokeBot could not save the package files. Check storage and try again.",
            PluginMarketplaceCommandRejectionCode.LifecycleRejected => LifecycleRejection(
                rejected.LifecycleRejection
            ),
        };

    private static string LifecycleRejection(PluginLifecycleCommandRejectionCode? code) =>
        code switch
        {
            PluginLifecycleCommandRejectionCode.NotFound =>
                "This plugin is not installed. Reload the status.",
            PluginLifecycleCommandRejectionCode.Busy =>
                "Another plugin operation is active. Wait for it to finish.",
            PluginLifecycleCommandRejectionCode.AlreadyActive =>
                "This plugin release is already active. Reload the status.",
            PluginLifecycleCommandRejectionCode.FaultedInstallation =>
                "This release is faulted. Apply another release or restart the plugin.",
            PluginLifecycleCommandRejectionCode.NotFaulted =>
                "This plugin is not faulted. Reload the status.",
            PluginLifecycleCommandRejectionCode.Conflict =>
                "The plugin state changed. Reload the status and try again.",
            PluginLifecycleCommandRejectionCode.InvalidPackageIdentity =>
                "The package identity is invalid. Choose another catalogue release.",
            PluginLifecycleCommandRejectionCode.GenerationExhausted =>
                "The plugin used all process generations. Restart BlokeBot before another operation.",
            null => "The plugin operation was rejected. Reload the status and try again.",
        };

    internal static string RefreshFailure(PluginMarketplaceRefreshFailureCode failure) =>
        failure switch
        {
            PluginMarketplaceRefreshFailureCode.DownloadFailed =>
                "The plugin repository download failed.",
            PluginMarketplaceRefreshFailureCode.RepositoryInvalid =>
                "The plugin repository layout is invalid.",
            PluginMarketplaceRefreshFailureCode.InvalidManifest =>
                "The plugin repository contains an invalid manifest.",
            PluginMarketplaceRefreshFailureCode.DuplicatePlugin =>
                "The plugin repository contains a duplicate plugin.",
        };
}
