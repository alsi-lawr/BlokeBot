namespace BlokeBot.Commands;

/// <summary>
/// Identifies a route which is reserved by the static chat dispatcher.
/// </summary>
public readonly record struct FixedChatCommandRoute
{
    public FixedChatCommandRoute(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = CommandAliasNormalizer.Normalize(value);
    }

    public string Value { get; }
}

/// <summary>
/// Owns the finite static route identities shared by dispatch and alias validation.
/// </summary>
public static class FixedChatCommandRoutes
{
    public static FixedChatCommandRoute Request { get; } = new("request");
    public static FixedChatCommandRoute Requests { get; } = new("requests");
    public static FixedChatCommandRoute RequestVote { get; } = new("requestvote");
    public static FixedChatCommandRoute RequestApprove { get; } = new("requestapprove");
    public static FixedChatCommandRoute RequestReject { get; } = new("requestreject");
    public static FixedChatCommandRoute RequestQueue { get; } = new("requestqueue");
    public static FixedChatCommandRoute RequestAccept { get; } = new("requestaccept");
    public static FixedChatCommandRoute RequestComplete { get; } = new("requestcomplete");
    public static FixedChatCommandRoute RequestMerge { get; } = new("requestmerge");
    public static FixedChatCommandRoute Queue { get; } = new("queue");
    public static FixedChatCommandRoute Join { get; } = new("join");
    public static FixedChatCommandRoute Leave { get; } = new("leave");
    public static FixedChatCommandRoute Position { get; } = new("position");
    public static FixedChatCommandRoute Ready { get; } = new("ready");
    public static FixedChatCommandRoute QueueOpen { get; } = new("queueopen");
    public static FixedChatCommandRoute QueueClose { get; } = new("queueclose");
    public static FixedChatCommandRoute Moment { get; } = new("moment");
    public static FixedChatCommandRoute Clip { get; } = new("clip");

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            [
                Request.Value,
                Requests.Value,
                RequestVote.Value,
                RequestApprove.Value,
                RequestReject.Value,
                RequestQueue.Value,
                RequestAccept.Value,
                RequestComplete.Value,
                RequestMerge.Value,
                Queue.Value,
                Join.Value,
                Leave.Value,
                Position.Value,
                Ready.Value,
                QueueOpen.Value,
                QueueClose.Value,
                Moment.Value,
                Clip.Value,
            ],
            StringComparer.OrdinalIgnoreCase
        );

    public static string? FindCollision(IEnumerable<string> aliases)
    {
        return aliases.FirstOrDefault(All.Contains);
    }
}
