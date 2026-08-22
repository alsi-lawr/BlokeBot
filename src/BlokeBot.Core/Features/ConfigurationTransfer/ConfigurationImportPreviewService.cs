using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationImportPreviewService
{
    private readonly IDbContextFactory<BlokeBotDbContext> _dbFactory;
    private readonly IOverlayConfigurationTransferAdapter _overlays;
    private readonly IAutomationConfigurationTransferAdapter _automations;

    public ConfigurationImportPreviewService(IDbContextFactory<BlokeBotDbContext> dbFactory)
        : this(
            dbFactory,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            UnavailableAutomationConfigurationTransferAdapter.Instance
        ) { }

    internal ConfigurationImportPreviewService(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        IOverlayConfigurationTransferAdapter overlays,
        IAutomationConfigurationTransferAdapter automations
    )
    {
        _dbFactory = dbFactory;
        _overlays = overlays;
        _automations = automations;
    }

    public async Task<ConfigurationPreviewOutcome> PreviewAsync(
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == selection.DestinationHostId, cancellationToken);
        if (host is null)
        {
            return new ConfigurationPreviewOutcome.HostNotFound();
        }
        if (ConfigurationDocumentValidator.Validate(document) is { } documentIssue)
        {
            return new ConfigurationPreviewOutcome.Success(
                new(
                    Guid.NewGuid(),
                    host.Id,
                    host.Login,
                    document,
                    selection
                        .Sections.Select(
                            (selected, index) =>
                                new ConfigurationSectionPreview(
                                    selected.Section,
                                    new(0, 0, 0, 0),
                                    index == 0 ? [documentIssue] : [],
                                    []
                                )
                        )
                        .ToArray(),
                    []
                )
            );
        }

        var references = await ConfigurationImportReferencePlan.BuildAsync(
            db,
            host.Id,
            document,
            selection,
            cancellationToken
        );

        var previews = new List<ConfigurationSectionPreview>();
        foreach (var selected in selection.Sections)
        {
            previews.Add(
                await PreviewSectionAsync(
                    db,
                    host,
                    document,
                    selected,
                    selection,
                    references,
                    cancellationToken
                )
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
