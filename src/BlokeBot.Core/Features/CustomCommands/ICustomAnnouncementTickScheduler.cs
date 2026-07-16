namespace BlokeBot.Core.Features.CustomCommands;

public interface ICustomAnnouncementTickScheduler
{
    DateTimeOffset GetUtcNow();

    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

internal sealed class TimeProviderCustomAnnouncementTickScheduler(TimeProvider timeProvider)
    : ICustomAnnouncementTickScheduler
{
    public DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow();
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        return Task.Delay(delay, timeProvider, ct);
    }
}
