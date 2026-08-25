namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal interface IConfigurationImportObserverDispatcher
{
    Task<ConfigurationPostCommitReport> DispatchAsync(
        int hostId,
        IReadOnlySet<ConfigurationSectionId> changedSections
    );
}

internal sealed record ConfigurationPostCommitReport(
    IReadOnlyList<ConfigurationPostCommitFailure> Failures,
    IReadOnlyList<ConfigurationImportManualFollowUp> ManualFollowUps
)
{
    internal static ConfigurationPostCommitReport Empty { get; } = new([], []);
}

internal sealed class ConfigurationImportObserverDispatcher(
    IEnumerable<IConfigurationImportObserver> observers,
    ILogger<ConfigurationImportObserverDispatcher> logger
) : IConfigurationImportObserverDispatcher
{
    public async Task<ConfigurationPostCommitReport> DispatchAsync(
        int hostId,
        IReadOnlySet<ConfigurationSectionId> changedSections
    )
    {
        var failures = new List<ConfigurationPostCommitFailure>();
        var manualFollowUps = new List<ConfigurationImportManualFollowUp>();
        foreach (var observer in observers.Where(value => changedSections.Contains(value.Section)))
        {
            try
            {
                var observation = await observer.ImportedAsync(hostId, CancellationToken.None);
                manualFollowUps.AddRange(observation.ManualFollowUps);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Configuration import post-commit observer failed for {Section} on host {HostId}.",
                    observer.Section,
                    hostId
                );
                failures.Add(new(observer.Section, "reconciliation-failed"));
            }
        }
        return new(failures, manualFollowUps);
    }
}

internal sealed class UnavailableConfigurationImportObserverDispatcher
    : IConfigurationImportObserverDispatcher
{
    internal static UnavailableConfigurationImportObserverDispatcher Instance { get; } = new();

    public Task<ConfigurationPostCommitReport> DispatchAsync(
        int hostId,
        IReadOnlySet<ConfigurationSectionId> changedSections
    ) => Task.FromResult(ConfigurationPostCommitReport.Empty);
}
