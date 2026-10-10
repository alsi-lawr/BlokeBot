using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class DashboardConfigurationTransferAdapter
{
    internal static async Task<ConfigurationSectionPreview> PreviewAsync(
        BlokeBotDbContext db,
        int hostId,
        DashboardsSectionV1? section,
        CancellationToken ct
    )
    {
        if (section is null)
        {
            return new(
                ConfigurationSectionId.Dashboards,
                new(0, 0, 0, 0),
                [new("sections.dashboards", "The selected section is not present in the file.")],
                []
            );
        }
        var targets = await DashboardTransferTargets.LoadAsync(db, hostId, ct);
        var issues = section
            .Layouts.SelectMany(layout =>
                DashboardConfigurationImportMapper
                    .Map(
                        layout,
                        targets,
                        new Dictionary<string, DashboardAutomationImportMap>(),
                        new Dictionary<string, GuessRoundProfile>()
                    )
                    .Widgets.Where(widget =>
                        widget.Configuration.Binding is DashboardBinding.Unconfigured
                    )
                    .Select(widget => new ConfigurationValidationIssue(
                        $"sections.dashboards[{layout.Id}]",
                        $"{widget.Label}: {((DashboardBinding.Unconfigured)widget.Configuration.Binding).Reference ?? "Choose a target in Dashboard Editor"}",
                        false
                    ))
            )
            .ToArray();
        return new(
            ConfigurationSectionId.Dashboards,
            new(section.Layouts.Count, 0, 0, 0),
            issues,
            []
        );
    }

    internal static async Task StageAsync(
        BlokeBotDbContext db,
        int hostId,
        DashboardsSectionV1 section,
        IReadOnlyDictionary<string, DashboardAutomationImportMap> appliedFlows,
        IReadOnlyDictionary<string, GuessRoundProfile> importedProfiles,
        CancellationToken ct
    )
    {
        var targets = await DashboardTransferTargets.LoadAsync(db, hostId, ct);
        var names = (
            await db
                .ChannelDashboards.Where(row => row.HostId == hostId)
                .Select(row => row.Name)
                .ToArrayAsync(ct)
        )
            .Select(ConfigurationImportReferencePlan.NormalizeName)
            .ToHashSet();
        foreach (var layout in section.Layouts)
        {
            var name = layout.Name.Trim();
            for (
                var copy = 2;
                !names.Add(ConfigurationImportReferencePlan.NormalizeName(name));
                copy++
            )
            {
                name = $"{layout.Name.Trim()} ({copy})";
            }
            _ = db.ChannelDashboards.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    HostId = hostId,
                    Name = name,
                    Revision = 1,
                    DocumentJson = DashboardDocuments.Serialize(
                        DashboardConfigurationImportMapper.Map(
                            layout,
                            targets,
                            appliedFlows,
                            importedProfiles
                        )
                    ),
                }
            );
        }
    }
}
