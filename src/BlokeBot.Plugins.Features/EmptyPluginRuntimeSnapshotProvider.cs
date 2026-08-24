using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed class EmptyPluginRuntimeSnapshotProvider : IPluginRuntimeSnapshotProvider
{
    public PluginRuntimeSnapshot Current => PluginRuntimeSnapshot.Empty;

    public PluginAdmissionOutcome Admit(
        PluginId pluginId,
        PluginLifecycleFence expected,
        PluginFeatureAdmissionReadiness readiness
    ) => new PluginAdmissionOutcome.Rejected(PluginAdmissionRejectionCode.Missing);

    public PluginAdmissionOutcome AdmitDurableRun(
        PluginId pluginId,
        PluginLifecycleFence expected,
        PluginFeatureAdmissionReadiness readiness
    ) => Admit(pluginId, expected, readiness);

    public PluginFenceOutcome ValidateCallbackCompletion(
        PluginId pluginId,
        PluginLifecycleFence fence
    ) => new PluginFenceOutcome.Rejected(PluginFenceRejectionCode.Missing);

    public PluginFenceOutcome ValidateWorkerResult(PluginId pluginId, PluginLifecycleFence fence) =>
        new PluginFenceOutcome.Rejected(PluginFenceRejectionCode.Missing);

    public PluginFenceOutcome ValidateCancellation(PluginId pluginId, PluginLifecycleFence fence) =>
        new PluginFenceOutcome.Rejected(PluginFenceRejectionCode.Missing);
}
