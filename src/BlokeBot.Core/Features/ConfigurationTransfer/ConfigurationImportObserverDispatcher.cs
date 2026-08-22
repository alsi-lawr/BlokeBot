namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal interface IConfigurationImportObserverDispatcher
{
    Task<IReadOnlyList<ConfigurationPostCommitFailure>> DispatchAsync(
        int hostId,
        IReadOnlySet<ConfigurationSectionId> changedSections
    );
}

internal sealed class ConfigurationImportObserverDispatcher(
    IEnumerable<IConfigurationImportObserver> observers,
    ILogger<ConfigurationImportObserverDispatcher> logger
) : IConfigurationImportObserverDispatcher
{
    public async Task<IReadOnlyList<ConfigurationPostCommitFailure>> DispatchAsync(
        int hostId,
        IReadOnlySet<ConfigurationSectionId> changedSections
    )
    {
        var failures = new List<ConfigurationPostCommitFailure>();
        foreach (var observer in observers.Where(value => changedSections.Contains(value.Section)))
        {
            try
            {
                await observer.ImportedAsync(hostId, CancellationToken.None);
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
        return failures;
    }
}

internal sealed class UnavailableConfigurationImportObserverDispatcher
    : IConfigurationImportObserverDispatcher
{
    internal static UnavailableConfigurationImportObserverDispatcher Instance { get; } = new();

    public Task<IReadOnlyList<ConfigurationPostCommitFailure>> DispatchAsync(
        int hostId,
        IReadOnlySet<ConfigurationSectionId> changedSections
    ) => Task.FromResult<IReadOnlyList<ConfigurationPostCommitFailure>>([]);
}
