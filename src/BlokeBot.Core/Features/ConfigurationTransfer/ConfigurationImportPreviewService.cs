using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationImportPreviewService(
    IDbContextFactory<BlokeBotDbContext> dbFactory
)
{
    public async Task<ConfigurationPreviewOutcome> PreviewAsync(
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == selection.DestinationHostId, cancellationToken);
        if (host is null)
        {
            return new ConfigurationPreviewOutcome.HostNotFound();
        }

        var previews = new List<ConfigurationSectionPreview>();
        foreach (var selected in selection.Sections)
        {
            previews.Add(
                await PreviewSectionAsync(db, host, document, selected, cancellationToken)
            );
        }
        var enablement = document.Sections.ChannelToolEnablement is { } imported
            ? ChannelToolEnablementMapper
                .Changes(host.EnabledFeatures, imported)
                .Select(x => new ConfigurationEnablementChange(
                    x.Feature,
                    host.EnabledFeatures.Contains(x.Feature),
                    x.Enabled,
                    selection.EnablementChanges.Contains(x.Feature)
                ))
                .ToArray()
            : [];
        return new ConfigurationPreviewOutcome.Success(
            new(Guid.NewGuid(), host.Id, host.Login, document, previews, enablement)
        );
    }

    private static ConfigurationSectionPreview Missing(ConfigurationSectionId section) =>
        new(
            section,
            new(0, 0, 0, 0),
            [new($"sections.{section}", "The selected section is not present in the file.")],
            []
        );
}

public abstract record ConfigurationPreviewOutcome
{
    private ConfigurationPreviewOutcome() { }

    public sealed record Success(ConfigurationImportPreview Preview) : ConfigurationPreviewOutcome;

    public sealed record HostNotFound : ConfigurationPreviewOutcome;
}
