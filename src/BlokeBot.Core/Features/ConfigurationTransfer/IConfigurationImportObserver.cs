namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal interface IConfigurationImportObserver
{
    ConfigurationSectionId Section { get; }

    ValueTask<ConfigurationImportObservation> ImportedAsync(
        int hostId,
        CancellationToken cancellationToken
    );
}

public sealed record ConfigurationPostCommitFailure(ConfigurationSectionId Section, string Code);

public sealed record ConfigurationImportManualFollowUp(
    string Code,
    string Title,
    string Reason,
    string LinkPath
);

internal sealed record ConfigurationImportObservation(
    IReadOnlyList<ConfigurationImportManualFollowUp> ManualFollowUps
)
{
    internal static ConfigurationImportObservation Complete { get; } = new([]);
}
