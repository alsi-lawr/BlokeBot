using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class AutomationPortableDependencies
{
    internal static IReadOnlyList<AutomationSubflowV2> Order(
        IReadOnlyList<AutomationSubflowV2> revisions,
        IReadOnlyList<AutomationFlowV2> flows
    )
    {
        if (
            revisions.Count > 128
            || revisions.Select(revision => revision.Id).Distinct(StringComparer.Ordinal).Count()
                != revisions.Count
        )
        {
            throw Invalid("Use at most 128 distinct subflows.");
        }
        var byId = revisions.ToDictionary(revision => revision.Id, StringComparer.Ordinal);
        var ordered = new List<AutomationSubflowV2>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new HashSet<string>(StringComparer.Ordinal);
        var visits = 0;
        foreach (var flow in flows.OrderBy(flow => flow.Id, StringComparer.Ordinal))
        {
            foreach (var call in Calls(flow).Order(StringComparer.Ordinal))
            {
                Visit(call, 0);
            }
        }
        return visited.Count == revisions.Count
            ? ordered
            : throw Invalid("Remove subflows that are not reachable from a selected flow.");

        void Visit(string id, int depth)
        {
            if (!byId.TryGetValue(id, out var revision))
            {
                throw Invalid($"Include missing subflow '{id}'.");
            }
            if (depth >= 8 || ++visits > 1024 || !stack.Add(revision.Id))
            {
                throw Invalid("Remove recursive subflow calls or reduce dependency depth.");
            }
            foreach (var call in Calls(revision.Graph).Order(StringComparer.Ordinal))
            {
                Visit(call, depth + 1);
            }
            _ = stack.Remove(revision.Id);
            if (visited.Add(id))
            {
                ordered.Add(revision);
            }
        }
    }

    internal static IEnumerable<string> Calls(AutomationFlowV2 graph) =>
        graph.Nodes.Select(node => node.Subflow?.SubflowId).OfType<string>();

    private static AutomationConfigurationExportException Invalid(string reason) =>
        new("subflow", reason);
}
