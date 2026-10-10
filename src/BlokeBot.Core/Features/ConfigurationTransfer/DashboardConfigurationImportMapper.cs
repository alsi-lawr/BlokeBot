using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class DashboardConfigurationImportMapper
{
    internal static DashboardDocument Map(
        DashboardLayoutV1 layout,
        DashboardTransferTargets targets,
        IReadOnlyDictionary<string, DashboardAutomationImportMap> appliedFlows,
        IReadOnlyDictionary<string, GuessRoundProfile> importedProfiles
    ) =>
        new(
            layout.Columns,
            [
                .. layout.Widgets.Select(widget => new DashboardWidget(
                    new(Guid.NewGuid()),
                    widget.Label,
                    new(
                        widget.Placement.Column,
                        widget.Placement.Row,
                        widget.Placement.Width,
                        widget.Placement.Height,
                        widget.Placement.Order
                    ),
                    Configuration(
                        widget.Configuration,
                        Resolve(
                            widget.Configuration.Binding,
                            targets,
                            appliedFlows,
                            importedProfiles
                        )
                    )
                )),
            ]
        );

    internal static DashboardWidgetConfiguration Configuration(
        DashboardConfigurationV1 value,
        DashboardBinding binding
    ) =>
        value switch
        {
            DashboardConfigurationV1.Automation automation =>
                new DashboardWidgetConfiguration.Automation(
                    binding,
                    automation.TextOverride is null
                        ? new DashboardTextOverride.Inherit()
                        : new DashboardTextOverride.Replace(automation.TextOverride)
                ),
            DashboardConfigurationV1.Value display => new DashboardWidgetConfiguration.Value(
                binding,
                display.DictionaryKey is null
                    ? new DashboardValueSelection.Scalar()
                    : new DashboardValueSelection.DictionaryEntry(display.DictionaryKey)
            ),
            DashboardConfigurationV1.Countdown countdown =>
                new DashboardWidgetConfiguration.Countdown(
                    binding,
                    TimeSpan.FromSeconds(countdown.DefaultSeconds)
                ),
            DashboardConfigurationV1.Poll => new DashboardWidgetConfiguration.Poll(binding),
            DashboardConfigurationV1.Prediction => new DashboardWidgetConfiguration.Prediction(
                binding
            ),
            DashboardConfigurationV1.Giveaway => new DashboardWidgetConfiguration.Giveaway(binding),
            DashboardConfigurationV1.Guessing => new DashboardWidgetConfiguration.Guessing(binding),
            _ => throw new System.Diagnostics.UnreachableException(),
        };

    private static DashboardBinding Resolve(
        DashboardReferenceV1 reference,
        DashboardTransferTargets targets,
        IReadOnlyDictionary<string, DashboardAutomationImportMap> appliedFlows,
        IReadOnlyDictionary<string, GuessRoundProfile> importedProfiles
    ) =>
        reference switch
        {
            DashboardReferenceV1.Automation source => Automation(source, targets, appliedFlows),
            DashboardReferenceV1.Value value => Unique(
                targets
                    .Values.Where(row =>
                        row.NameHash == CustomValueIdentity.Hash(value.Name)
                        && row.Scope == value.Scope
                        && row.Kind == value.Kind
                    )
                    .Select(row => new DashboardTarget.Value(row.Id)),
                $"Stored value '{value.Name}'"
            ),
            DashboardReferenceV1.Countdown timer => new DashboardBinding.Configured(
                new DashboardTarget.Countdown(timer.Name)
            ),
            DashboardReferenceV1.Poll poll => Unique(
                targets
                    .Polls.Where(row => SameName(row.Title, poll.Title))
                    .Select(row => new DashboardTarget.Poll(row.Id)),
                $"Poll template '{poll.Title}'"
            ),
            DashboardReferenceV1.Prediction prediction => Unique(
                targets
                    .Predictions.Where(row => SameName(row.Title, prediction.Title))
                    .Select(row => new DashboardTarget.Prediction(row.Id)),
                $"Prediction template '{prediction.Title}'"
            ),
            DashboardReferenceV1.Giveaway => new DashboardBinding.Configured(
                new DashboardTarget.Giveaway()
            ),
            DashboardReferenceV1.Guessing profile
                when profile.DocumentProfileId is { } id
                    && importedProfiles.TryGetValue(id, out var mapped) =>
                new DashboardBinding.Configured(new DashboardTarget.Guessing(mapped.Id)),
            DashboardReferenceV1.Guessing profile => Unique(
                targets
                    .Profiles.Where(row => row.Slug == profile.Slug)
                    .Select(row => new DashboardTarget.Guessing(row.Id)),
                $"Guessing profile '{profile.Slug}'"
            ),
            DashboardReferenceV1.Unconfigured unresolved => new DashboardBinding.Unconfigured(
                unresolved.Issue switch
                {
                    DashboardReferenceIssueV1.NotSelected => DashboardBindingIssue.NotSelected,
                    DashboardReferenceIssueV1.Missing => DashboardBindingIssue.Missing,
                    DashboardReferenceIssueV1.Ambiguous => DashboardBindingIssue.Ambiguous,
                },
                unresolved.Reason
            ),
            _ => throw new System.Diagnostics.UnreachableException(),
        };

    private static DashboardBinding Automation(
        DashboardReferenceV1.Automation source,
        DashboardTransferTargets targets,
        IReadOnlyDictionary<string, DashboardAutomationImportMap> appliedFlows
    )
    {
        if (
            source.DocumentSource is { } document
            && appliedFlows.TryGetValue(document.FlowId, out var map)
        )
        {
            var flow = targets.Flows.SingleOrDefault(row => row.Id == map.FlowId.Value);
            return
                map.Nodes.TryGetValue(document.NodeId, out var nodeId)
                && flow?.Nodes.Any(node =>
                    node.Id == nodeId.Value
                    && node.DefinitionId == AutomationDefinitionIds.ManualSource.Value
                ) == true
                ? new DashboardBinding.Configured(
                    new DashboardTarget.Automation(map.FlowId, nodeId)
                )
                : Unresolved(0, $"Manual Run source in '{source.FlowName}'");
        }
        var flows = targets.Flows.Where(row => SameName(row.Name, source.FlowName)).ToArray();
        if (flows.Length != 1)
        {
            return Unresolved(flows.Length, $"Automation '{source.FlowName}'");
        }
        var nodes = flows[0]
            .Nodes.Where(node => node.DefinitionId == AutomationDefinitionIds.ManualSource.Value)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(source.SourceAlias))
        {
            nodes = nodes
                .Where(node => SameName(node.DisplayAlias ?? "", source.SourceAlias))
                .ToArray();
        }
        return Unique(
            nodes.Select(node => new DashboardTarget.Automation(new(flows[0].Id), new(node.Id))),
            $"Manual Run source in '{source.FlowName}'"
        );
    }

    private static DashboardBinding Unique<T>(IEnumerable<T> candidates, string reference)
        where T : DashboardTarget
    {
        var matches = candidates.Take(2).ToArray();
        return matches.Length == 1
            ? new DashboardBinding.Configured(matches[0])
            : Unresolved(matches.Length, reference);
    }

    private static DashboardBinding Unresolved(int count, string reference) =>
        new DashboardBinding.Unconfigured(
            count == 0 ? DashboardBindingIssue.Missing : DashboardBindingIssue.Ambiguous,
            $"{reference} {(count == 0 ? "was not found" : "matches several targets")}. Choose a destination target to rebind it"
        );

    private static bool SameName(string left, string right) =>
        ConfigurationImportReferencePlan.NormalizeName(left)
        == ConfigurationImportReferencePlan.NormalizeName(right);
}
