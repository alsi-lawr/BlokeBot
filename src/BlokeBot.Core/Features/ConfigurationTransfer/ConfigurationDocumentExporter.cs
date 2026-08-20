using System.Reflection;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed class ConfigurationDocumentExporter(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ConfigurationDocumentCodec codec,
    TimeProvider timeProvider
)
{
    public async Task<ConfigurationExportOutcome> ExportAsync(
        int hostId,
        IReadOnlySet<ConfigurationSectionId> selectedSections,
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
        if ((host.EnabledFeatures & ~Persistence.Models.HostFeatureFlags.All) != 0)
        {
            return new ConfigurationExportOutcome.Unsupported(
                "Channel tool enablement contains a flag that format 1 cannot represent."
            );
        }

        var commandGraph =
            selectedSections.Contains(ConfigurationSectionId.CustomCommands)
            || selectedSections.Contains(ConfigurationSectionId.Announcements)
                ? await ConfigurationExportMappers.LoadCommandGraphAsync(
                    db,
                    hostId,
                    cancellationToken
                )
                : null;
        var document = new ConfigurationDocumentV1(
            ConfigurationDocumentCodec.Format,
            ConfigurationDocumentCodec.CurrentVersion,
            timeProvider.GetUtcNow(),
            new(host.Login, CurrentVersion()),
            new(
                selectedSections.Contains(ConfigurationSectionId.CustomCommands)
                    ? ConfigurationExportMappers.CustomCommands(commandGraph!, host.TimeZoneId)
                    : null,
                selectedSections.Contains(ConfigurationSectionId.Announcements)
                    ? ConfigurationExportMappers.Announcements(commandGraph!)
                    : null,
                selectedSections.Contains(ConfigurationSectionId.Guessing)
                    ? await ConfigurationExportMappers.GuessingAsync(db, hostId, cancellationToken)
                    : null,
                selectedSections.Contains(ConfigurationSectionId.Points)
                    ? await ConfigurationExportMappers.PointsAsync(db, hostId, cancellationToken)
                    : null,
                selectedSections.Contains(ConfigurationSectionId.ChannelToolEnablement)
                    ? ChannelToolEnablementMapper.FromFlags(host.EnabledFeatures)
                    : null
            )
        );
        return new ConfigurationExportOutcome.Success(document, codec.Serialize(document));
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
