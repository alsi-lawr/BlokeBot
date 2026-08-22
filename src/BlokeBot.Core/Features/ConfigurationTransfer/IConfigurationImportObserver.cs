namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal interface IConfigurationImportObserver
{
    ConfigurationSectionId Section { get; }

    ValueTask ImportedAsync(int hostId, CancellationToken cancellationToken);
}

public sealed record ConfigurationPostCommitFailure(ConfigurationSectionId Section, string Code);
