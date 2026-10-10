using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Dashboards;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationDocumentValidator
{
    internal static ConfigurationValidationIssue? ValidateDashboards(
        ConfigurationDocumentV2 document
    )
    {
        if (document.Sections.Dashboards is not { } section)
        {
            return null;
        }
        if (section.Layouts.Any(layout => layout is null))
        {
            return new(
                "sections.dashboards.layouts",
                "A dashboard layout must be an object, not null."
            );
        }
        if (
            (
                Limit("sections.dashboards.layouts", section.Layouts.Count)
                ?? DuplicateIds(
                    "sections.dashboards.layouts",
                    section.Layouts.Select(layout => layout.Id)
                )
            ) is
            { } issue
        )
        {
            return issue;
        }
        foreach (var layout in section.Layouts)
        {
            var path = $"sections.dashboards.layouts[{layout.Id}]";
            if (string.IsNullOrWhiteSpace(layout.Name))
            {
                return new(path, "A dashboard name is required.");
            }
            if (layout.Widgets.Any(widget => widget is null))
            {
                return new(path + ".widgets", "A dashboard widget must be an object, not null.");
            }
            if (
                (
                    Limit(path + ".widgets", layout.Widgets.Count)
                    ?? DuplicateIds(path + ".widgets", layout.Widgets.Select(widget => widget.Id))
                ) is
                { } widgetsIssue
            )
            {
                return widgetsIssue;
            }
            foreach (var widget in layout.Widgets)
            {
                if (
                    !ValidReference(widget.Configuration)
                    || !ValidDocumentSource(widget.Configuration.Binding, document)
                )
                {
                    return new(
                        path,
                        "Use a compatible portable target reference and an exported Manual Run source."
                    );
                }
            }
            var local = DashboardConfigurationImportMapper.Map(
                layout,
                new([], [], [], [], []),
                new Dictionary<string, DashboardAutomationImportMap>(),
                new Dictionary<string, BlokeBot.Persistence.Models.GuessRoundProfile>()
            );
            if (!DashboardDocuments.Valid(local))
            {
                return new(path, "Check widget settings and grid positions.");
            }
        }
        return null;
    }

    private static bool ValidReference(DashboardConfigurationV1 configuration)
    {
        var reference = configuration.Binding;
        return reference is DashboardReferenceV1.Unconfigured unresolved
            ? Enum.IsDefined(unresolved.Issue)
            : configuration switch
            {
                DashboardConfigurationV1.Automation => reference
                    is DashboardReferenceV1.Automation flow
                    && !string.IsNullOrWhiteSpace(flow.FlowName),
                DashboardConfigurationV1.Value => reference is DashboardReferenceV1.Value value
                    && !string.IsNullOrWhiteSpace(value.Name)
                    && Enum.IsDefined(value.Scope)
                    && Enum.IsDefined(value.Kind),
                DashboardConfigurationV1.Countdown => reference
                    is DashboardReferenceV1.Countdown timer
                    && !string.IsNullOrWhiteSpace(timer.Name)
                    && timer.Name.Length <= 96,
                DashboardConfigurationV1.Poll => reference is DashboardReferenceV1.Poll poll
                    && !string.IsNullOrWhiteSpace(poll.Title),
                DashboardConfigurationV1.Prediction => reference
                    is DashboardReferenceV1.Prediction prediction
                    && !string.IsNullOrWhiteSpace(prediction.Title),
                DashboardConfigurationV1.Giveaway => reference is DashboardReferenceV1.Giveaway,
                DashboardConfigurationV1.Guessing => reference
                    is DashboardReferenceV1.Guessing profile
                    && !string.IsNullOrWhiteSpace(profile.Slug),
                _ => throw new System.Diagnostics.UnreachableException(),
            };
    }

    private static bool ValidDocumentSource(
        DashboardReferenceV1 reference,
        ConfigurationDocumentV2 document
    ) =>
        reference switch
        {
            DashboardReferenceV1.Automation { DocumentSource: { } source } => document
                .Sections.Automations?.Flows.SingleOrDefault(flow => flow.Id == source.FlowId)
                ?.Nodes.Any(node =>
                    node.Id == source.NodeId
                    && node.DefinitionId == AutomationDefinitionIds.ManualSource.Value
                ) == true,
            DashboardReferenceV1.Guessing { DocumentProfileId: { } id } =>
                document.Sections.Guessing?.Profiles.Any(profile => profile.Id == id) == true,
            _ => true,
        };
}
