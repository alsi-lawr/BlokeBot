using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed record DashboardAutomationExportMap(
    string FlowId,
    IReadOnlyDictionary<AutomationNodeId, string> Nodes
);

internal sealed record AutomationConfigurationExport(
    AutomationsSectionV2 Section,
    IReadOnlyDictionary<AutomationFlowId, DashboardAutomationExportMap> Flows
);

internal sealed record DashboardAutomationImportMap(
    AutomationFlowId FlowId,
    IReadOnlyDictionary<string, AutomationNodeId> Nodes
);
