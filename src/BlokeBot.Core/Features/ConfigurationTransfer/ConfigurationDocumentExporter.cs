using System.Reflection;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed class ConfigurationDocumentExporter(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ConfigurationDocumentCodec codec,
    AutomationCatalogService automationCatalog,
    AutomationFlowService automationFlows,
    TimeProvider timeProvider,
    AutomationScenarioService scenarios
)
{
    public async Task<ConfigurationExportOutcome> ExportAsync(
        int hostId,
        ConfigurationExportSelection selection,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        if (host is null)
        {
            return new ConfigurationExportOutcome.NotFound();
        }
        if (
            selection.Sections.Contains(ConfigurationSectionId.ChannelToolEnablement)
            && !ChannelToolEnablementMapper.CanRepresent(host.EnabledFeatures)
        )
        {
            return new ConfigurationExportOutcome.Unsupported(
                "Channel tool enablement contains a flag that format 2 cannot represent."
            );
        }
        if (
            selection.Sections.Contains(ConfigurationSectionId.Overlays)
            && selection.Overlay.UrlLayers
            && !selection.Overlay.UrlWarningAcknowledged
        )
        {
            return new ConfigurationExportOutcome.Unsupported(
                "Confirm the URL warning before exporting complete Overlay URLs."
            );
        }

        if (
            selection.Sections.Contains(ConfigurationSectionId.Automations)
            && (
                selection.AutomationFlowIds.Count
                    > ConfigurationDocumentCodec.MaximumRecordsPerCollection
                || selection.AutomationScenarioIds.Count
                    > ConfigurationDocumentCodec.MaximumRecordsPerCollection
            )
        )
        {
            return new ConfigurationExportOutcome.Unsupported(
                "Select at most 1,000 flows and 1,000 scenarios for one file."
            );
        }

        var references = await ConfigurationExportReferencePlan.LoadAsync(
            db,
            hostId,
            cancellationToken
        );
        var commandGraph =
            selection.Sections.Contains(ConfigurationSectionId.CustomCommands)
            || selection.Sections.Contains(ConfigurationSectionId.Announcements)
                ? await ConfigurationExportMappers.LoadCommandGraphAsync(
                    db,
                    hostId,
                    cancellationToken
                )
                : null;
        try
        {
            var automations = selection.Sections.Contains(ConfigurationSectionId.Automations)
                ? await ConfigurationExportMappers.AutomationsAsync(
                    db,
                    hostId,
                    references,
                    automationCatalog,
                    automationFlows,
                    scenarios,
                    selection,
                    cancellationToken
                )
                : null;
            if (ConfigurationDocumentValidator.ValidateAutomations(automations) is { } issue)
            {
                return new ConfigurationExportOutcome.Unsupported(issue.Message);
            }
            var document = new ConfigurationDocumentV2(
                ConfigurationDocumentCodec.Format,
                ConfigurationDocumentCodec.CurrentVersion,
                timeProvider.GetUtcNow(),
                new(host.Login, CurrentVersion()),
                new(
                    selection.Sections.Contains(ConfigurationSectionId.CustomCommands)
                        ? ConfigurationExportMappers.CustomCommands(
                            commandGraph!,
                            host.TimeZoneId,
                            references
                        )
                        : null,
                    selection.Sections.Contains(ConfigurationSectionId.Announcements)
                        ? ConfigurationExportMappers.Announcements(commandGraph!)
                        : null,
                    selection.Sections.Contains(ConfigurationSectionId.Guessing)
                        ? await ConfigurationExportMappers.GuessingAsync(
                            db,
                            hostId,
                            cancellationToken
                        )
                        : null,
                    selection.Sections.Contains(ConfigurationSectionId.Points)
                        ? await ConfigurationExportMappers.PointsAsync(
                            db,
                            hostId,
                            cancellationToken
                        )
                        : null,
                    selection.Sections.Contains(ConfigurationSectionId.ChannelToolEnablement)
                        ? ChannelToolEnablementMapper.FromFlags(host.EnabledFeatures)
                        : null,
                    selection.Sections.Contains(ConfigurationSectionId.Overlays)
                        ? await ConfigurationExportMappers.OverlaysAsync(
                            db,
                            hostId,
                            references,
                            selection.Overlay,
                            cancellationToken
                        )
                        : null,
                    automations
                )
            );
            var json = codec.Serialize(document);
            return json.Length > ConfigurationDocumentCodec.MaximumBytes
                ? new ConfigurationExportOutcome.Unsupported(
                    "Select fewer items. The configuration file exceeds the 2 MB limit."
                )
                : new ConfigurationExportOutcome.Success(document, json);
        }
        catch (AutomationConfigurationExportException exception)
        {
            return new ConfigurationExportOutcome.Unsupported(
                $"Automation node '{exception.DefinitionId}' cannot be exported in Format 2. {exception.Reason}"
            );
        }
    }

    private static string CurrentVersion() =>
        typeof(ConfigurationDocumentExporter)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0-dev";
}

public abstract record ConfigurationExportOutcome
{
    private ConfigurationExportOutcome() { }

    public sealed record Success(ConfigurationDocumentV2 Document, byte[] Json)
        : ConfigurationExportOutcome;

    public sealed record NotFound : ConfigurationExportOutcome;

    public sealed record Unsupported(string Message) : ConfigurationExportOutcome;
}

public sealed record AutomationExportChoice(
    Guid Id,
    string Name,
    IReadOnlyList<AutomationScenarioExportChoice> Scenarios
);

public sealed record AutomationScenarioExportChoice(Guid Id, string Name);
