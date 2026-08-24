using System.Reflection;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed class ConfigurationDocumentExporter(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ConfigurationDocumentCodec codec,
    AutomationCatalogService automationCatalog,
    AutomationFlowService automationFlows,
    ILogger<ConfigurationDocumentExporter> logger,
    TimeProvider timeProvider,
    IPluginFeatureStore pluginFeatures
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
                "Channel tool enablement contains a flag that format 1 cannot represent."
            );
        }
        if (
            PluginHostId.TryCreate(hostId, out var pluginHostId)
            && await pluginFeatures.HasFormat1IncompatibleStateAsync(
                pluginHostId,
                cancellationToken
            )
        )
        {
            return new ConfigurationExportOutcome.Unsupported(
                "Format 1 cannot export plugin-owned settings, secrets, or feature state."
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
                    cancellationToken
                )
                : null;
            var document = new ConfigurationDocumentV1(
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
                    automations?.Section
                )
            );
            var json = codec.Serialize(document);
            if (automations is not null)
            {
                AutomationTransferDiagnostics.LogExport(logger, hostId, automations.Diagnostics);
            }
            return new ConfigurationExportOutcome.Success(document, json);
        }
        catch (Format1AutomationExportException exception)
        {
            return new ConfigurationExportOutcome.Unsupported(
                $"Automation node '{exception.DefinitionId}' is not a core Format 1 node."
            );
        }
        catch (Format1AutomationConfigurationExportException exception)
        {
            return new ConfigurationExportOutcome.Unsupported(
                $"Automation node '{exception.DefinitionId}' cannot be exported in Format 1. {exception.Reason}"
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

    public sealed record Success(ConfigurationDocumentV1 Document, byte[] Json)
        : ConfigurationExportOutcome;

    public sealed record NotFound : ConfigurationExportOutcome;

    public sealed record Unsupported(string Message) : ConfigurationExportOutcome;
}
