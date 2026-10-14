using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class DashboardConfigurationExportMapper
{
    internal static async Task<DashboardsSectionV1> ExportAsync(
        BlokeBotDbContext db,
        int hostId,
        AutomationConfigurationExport? automations,
        GuessingSectionV1? guessing,
        CancellationToken ct
    )
    {
        var targets = await DashboardTransferTargets.LoadAsync(db, hostId, ct);
        var rows = await db
            .ChannelDashboards.AsNoTracking()
            .Where(row => row.HostId == hostId)
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .ToArrayAsync(ct);
        return new(
            rows.Select(
                    (row, index) =>
                    {
                        var document = DashboardDocuments.Deserialize(row.DocumentJson);
                        var ids = document
                            .Widgets.Select(
                                (widget, position) =>
                                    (widget.Id, Portable: $"widget-{position + 1:D4}")
                            )
                            .ToDictionary(item => item.Id, item => item.Portable);
                        DashboardArrangementV1 Arrangement(DashboardArrangement arrangement) =>
                            new(
                                arrangement.Columns,
                                arrangement.CellHeight,
                                arrangement
                                    .Placements.Select(p => new DashboardWidgetPlacementV1(
                                        ids[p.WidgetId],
                                        new(
                                            p.Placement.Column,
                                            p.Placement.Row,
                                            p.Placement.Width,
                                            p.Placement.Height,
                                            p.Placement.Order
                                        )
                                    ))
                                    .ToArray()
                            );
                        return new DashboardLayoutV1(
                            $"dashboard-{index + 1:D4}",
                            row.Name,
                            document
                                .Widgets.Select(widget => new DashboardWidgetV1(
                                    ids[widget.Id],
                                    widget.Label,
                                    Configuration(
                                        widget.Configuration,
                                        targets,
                                        automations,
                                        guessing
                                    )
                                ))
                                .ToArray(),
                            Arrangement(document.Desktop),
                            Arrangement(document.Phone!)
                        );
                    }
                )
                .ToArray()
        );
    }

    private static DashboardConfigurationV1 Configuration(
        DashboardWidgetConfiguration configuration,
        DashboardTransferTargets targets,
        AutomationConfigurationExport? automations,
        GuessingSectionV1? guessing
    )
    {
        var binding = configuration.Binding.Match<DashboardReferenceV1>(
            configured => Reference(configured.Target, targets, automations, guessing),
            unresolved => new DashboardReferenceV1.Unconfigured(
                Issue(unresolved.Issue),
                unresolved.Reference
            )
        );
        return configuration.Match<DashboardConfigurationV1>(
            value => new DashboardConfigurationV1.Automation(
                binding,
                value.Data.Match<string?>(_ => null, text => text.Text)
            ),
            value => new DashboardConfigurationV1.Value(
                binding,
                value.Selection.Match<string?>(_ => null, entry => entry.Key)
            ),
            value => new DashboardConfigurationV1.Countdown(
                binding,
                (int)value.DefaultDuration.TotalSeconds
            ),
            _ => new DashboardConfigurationV1.Poll(binding),
            _ => new DashboardConfigurationV1.Prediction(binding),
            _ => new DashboardConfigurationV1.Giveaway(binding),
            _ => new DashboardConfigurationV1.Guessing(binding)
        );
    }

    private static DashboardReferenceV1 Reference(
        DashboardTarget target,
        DashboardTransferTargets targets,
        AutomationConfigurationExport? automations,
        GuessingSectionV1? guessing
    ) =>
        target.Match<DashboardReferenceV1>(
            source =>
            {
                var flow = targets.Flows.SingleOrDefault(flow => flow.Id == source.FlowId.Value);
                var node = flow?.Nodes.SingleOrDefault(node =>
                    node.Id == source.SourceNodeId.Value
                    && node.DefinitionId == AutomationDefinitionIds.ManualSource.Value
                );
                if (flow is null || node is null)
                {
                    return Missing("The selected Manual Run source is missing.");
                }
                DashboardAutomationSourceV1? document = null;
                if (
                    automations is not null
                    && automations.Flows.TryGetValue(source.FlowId, out var mapped)
                    && mapped.Nodes.TryGetValue(source.SourceNodeId, out var id)
                )
                {
                    document = new(mapped.FlowId, id);
                }
                return new DashboardReferenceV1.Automation(flow.Name, node.DisplayAlias, document);
            },
            value =>
                targets.Values.SingleOrDefault(row => row.Id == value.DefinitionId) is { } row
                    ? new DashboardReferenceV1.Value(row.Name, row.Scope, row.Kind)
                    : Missing("The selected stored value is missing."),
            timer => new DashboardReferenceV1.Countdown(timer.Name),
            poll =>
                targets.Polls.SingleOrDefault(row => row.Id == poll.TemplateId) is { } row
                    ? new DashboardReferenceV1.Poll(row.Title)
                    : Missing("The selected poll template is missing."),
            prediction =>
                targets.Predictions.SingleOrDefault(row => row.Id == prediction.TemplateId)
                    is { } row
                    ? new DashboardReferenceV1.Prediction(row.Title)
                    : Missing("The selected prediction template is missing."),
            _ => new DashboardReferenceV1.Giveaway(),
            profile =>
                targets.Profiles.SingleOrDefault(row => row.Id == profile.ProfileId) is { } row
                    ? new DashboardReferenceV1.Guessing(
                        row.Slug,
                        guessing
                            ?.Profiles.SingleOrDefault(imported => imported.Slug == row.Slug)
                            ?.Id
                    )
                    : Missing("The selected guessing profile is missing.")
        );

    private static DashboardReferenceV1 Missing(string reason) =>
        new DashboardReferenceV1.Unconfigured(DashboardReferenceIssueV1.Missing, reason);

    private static DashboardReferenceIssueV1 Issue(DashboardBindingIssue issue) =>
        issue switch
        {
            DashboardBindingIssue.NotSelected => DashboardReferenceIssueV1.NotSelected,
            DashboardBindingIssue.Missing => DashboardReferenceIssueV1.Missing,
            DashboardBindingIssue.Ambiguous => DashboardReferenceIssueV1.Ambiguous,
        };
}
