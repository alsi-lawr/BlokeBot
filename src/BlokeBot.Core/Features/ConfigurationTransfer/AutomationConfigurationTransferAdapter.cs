using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter(
    AutomationFlowService flows,
    AutomationCatalogService catalog,
    TimeProvider timeProvider
) : IAutomationConfigurationTransferAdapter
{
    public async Task<ConfigurationSectionPreview> PreviewAsync(
        BlokeBotDbContext db,
        BotHost host,
        AutomationsSectionV1? section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        if (section is null)
        {
            return new(
                ConfigurationSectionId.Automations,
                new(0, 0, 0, 0),
                [new("sections.automations", "The selected section is not present in the file.")],
                []
            );
        }
        var existing = await db
            .AutomationFlows.AsNoTracking()
            .Where(value => value.HostId == host.Id)
            .Select(value => new
            {
                value.Id,
                value.Name,
                HasRuns = db.AutomationFlowRuns.Any(run => run.FlowId == value.Id),
            })
            .ToArrayAsync(cancellationToken);
        var issues = new List<ConfigurationValidationIssue>();
        var drafts = await BuildDraftsAsync(
            db,
            host.Id,
            section,
            references,
            allowPlannedCommands: true,
            issues,
            cancellationToken
        );
        _ = await ValidateDraftsAsync(drafts, issues, cancellationToken);
        var counts = Counts(
            existing.Select(value => value.Name),
            section.Flows.Select(value => value.Name),
            selection.Strategy
        );
        var importedNames = section
            .Flows.Select(value => ConfigurationImportReferencePlan.NormalizeName(value.Name))
            .ToHashSet();
        var conflicts =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? existing
                    .Where(value =>
                        value.HasRuns
                        && !importedNames.Contains(
                            ConfigurationImportReferencePlan.NormalizeName(value.Name)
                        )
                    )
                    .Select(value => new ConfigurationImportConflict(
                        ConfigurationSectionId.Automations,
                        $"automation-flow-{value.Id:D}",
                        value.Name,
                        "This absent flow has retained runtime history and cannot be deleted.",
                        [ImportConflictResolution.Retain, ImportConflictResolution.Abort]
                    ))
                    .ToArray()
                : [];
        var retained = conflicts.Count(conflict =>
            selection.ItemResolutions.Any(resolution =>
                resolution.ImportedId == conflict.ImportedId
                && resolution.Resolution == ImportConflictResolution.Retain
            )
        );
        return new(
            ConfigurationSectionId.Automations,
            counts with
            {
                Remove = Math.Max(0, counts.Remove - retained),
            },
            issues,
            conflicts
        );
    }

    public async Task<AutomationConfigurationStageResult> StageAsync(
        BlokeBotDbContext db,
        BotHost host,
        AutomationsSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        var issues = new List<ConfigurationValidationIssue>();
        if (
            selection.ItemResolutions.Any(value =>
                value.Resolution == ImportConflictResolution.Abort
            )
        )
        {
            issues.Add(
                new("sections.automations", "The import was aborted by a conflict decision.")
            );
        }
        var drafts = await BuildDraftsAsync(
            db,
            host.Id,
            section,
            references,
            allowPlannedCommands: false,
            issues,
            cancellationToken
        );
        var diagnostics = await ValidateDraftsAsync(drafts, null, cancellationToken);
        if (selection.Strategy == ImportConflictStrategy.ReplaceSection)
        {
            var importedNames = section
                .Flows.Select(value => ConfigurationImportReferencePlan.NormalizeName(value.Name))
                .ToHashSet();
            var retained = selection
                .ItemResolutions.Where(value => value.Resolution == ImportConflictResolution.Retain)
                .Select(value => value.ImportedId)
                .ToHashSet(StringComparer.Ordinal);
            var historicalFlows = await db
                .AutomationFlows.AsNoTracking()
                .Where(value =>
                    value.HostId == host.Id
                    && db.AutomationFlowRuns.Any(run => run.FlowId == value.Id)
                )
                .Select(value => new { value.Id, value.Name })
                .ToArrayAsync(cancellationToken);
            if (
                historicalFlows.Any(value =>
                    !importedNames.Contains(
                        ConfigurationImportReferencePlan.NormalizeName(value.Name)
                    ) && !retained.Contains($"automation-flow-{value.Id:D}")
                )
            )
            {
                issues.Add(
                    new(
                        "sections.automations",
                        "Resolve every absent Automation flow with retained history before applying replacement."
                    )
                );
            }
        }
        if (issues.Count > 0)
        {
            return new(issues, []);
        }

        await StageDraftsAsync(
            db,
            host.Id,
            drafts.Select(static value => value.Draft).ToArray(),
            selection,
            cancellationToken
        );
        return new([], diagnostics);
    }

    private static ConfigurationPreviewCount Counts(
        IEnumerable<string> existing,
        IEnumerable<string> imported,
        ImportConflictStrategy strategy
    )
    {
        var current = existing.Select(ConfigurationImportReferencePlan.NormalizeName).ToHashSet();
        var incoming = imported.Select(ConfigurationImportReferencePlan.NormalizeName).ToHashSet();
        return new(
            incoming.Count(name => !current.Contains(name)),
            strategy == ImportConflictStrategy.AddMissing ? 0 : incoming.Count(current.Contains),
            strategy == ImportConflictStrategy.AddMissing ? incoming.Count(current.Contains) : 0,
            strategy == ImportConflictStrategy.ReplaceSection
                ? current.Count(name => !incoming.Contains(name))
                : 0
        );
    }
}
