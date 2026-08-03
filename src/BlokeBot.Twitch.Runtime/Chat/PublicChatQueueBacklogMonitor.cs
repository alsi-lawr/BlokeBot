namespace BlokeBot.Twitch.Runtime;

internal sealed class PublicChatQueueBacklogMonitor
{
    private readonly HashSet<string> _alertedChannels = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PublicChatQueueBacklog> CaptureAlerts(
        IReadOnlyList<PublicChatPendingMessage> pending,
        DateTimeOffset now,
        TimeSpan threshold,
        bool enabled
    )
    {
        if (!enabled || threshold <= TimeSpan.Zero)
        {
            return [];
        }

        List<PublicChatQueueBacklog>? alerts = null;
        foreach (var group in PendingByChannel(pending))
        {
            if (_alertedChannels.Contains(group.Channel))
            {
                continue;
            }

            var oldest = group.Messages.MinBy(x => x.EnqueuedAt);
            var age = now - oldest.EnqueuedAt;
            if (age < threshold)
            {
                continue;
            }

            _ = _alertedChannels.Add(group.Channel);
            alerts ??= [];
            alerts.Add(
                new PublicChatQueueBacklog(
                    group.Channel,
                    group.Messages.Count,
                    age,
                    oldest.EnqueuedAt
                )
            );
        }

        return alerts ?? [];
    }

    public TimeSpan? NextAlertDelay(
        IReadOnlyList<PublicChatPendingMessage> pending,
        DateTimeOffset now,
        TimeSpan threshold,
        bool enabled
    )
    {
        if (!enabled || threshold <= TimeSpan.Zero)
        {
            return null;
        }

        TimeSpan? next = null;
        foreach (var group in PendingByChannel(pending))
        {
            if (_alertedChannels.Contains(group.Channel))
            {
                continue;
            }

            var oldest = group.Messages.MinBy(x => x.EnqueuedAt);
            var remaining = threshold - (now - oldest.EnqueuedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            next = next is null ? remaining : Min(next.Value, remaining);
        }

        return next;
    }

    public void ResetDrainedChannels(IReadOnlyList<PublicChatPendingMessage> pending)
    {
        if (_alertedChannels.Count == 0)
        {
            return;
        }

        if (pending.Count == 0)
        {
            _alertedChannels.Clear();
            return;
        }

        var activeChannels = pending
            .Select(x => NormalizeChannel(x.Channel))
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ = _alertedChannels.RemoveWhere(channel => !activeChannels.Contains(channel));
    }

    private static List<PendingChannelGroup> PendingByChannel(
        IReadOnlyList<PublicChatPendingMessage> pending
    ) =>
        pending
            .GroupBy(x => NormalizeChannel(x.Channel), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new PendingChannelGroup(group.Key, group.ToList()))
            .ToList();

    private static string NormalizeChannel(string channel) => channel.Trim().ToLowerInvariant();

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private sealed record PendingChannelGroup(
        string Channel,
        List<PublicChatPendingMessage> Messages
    );
}
