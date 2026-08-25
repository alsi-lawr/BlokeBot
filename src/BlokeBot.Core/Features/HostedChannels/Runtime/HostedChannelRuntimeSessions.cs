namespace BlokeBot.Core.Features.HostedChannels.Runtime;

internal sealed class HostedChannelRuntimeSessions
{
    private readonly Dictionary<int, BotChannelSessionIdentity> _identities = [];

    internal BotChannelSessionIdentity GetOrCreate(int hostId)
    {
        if (!_identities.TryGetValue(hostId, out var identity))
        {
            identity = BotChannelSessionIdentity.Create();
            _identities[hostId] = identity;
        }

        return identity;
    }

    internal bool IsCurrent(int hostId, BotChannelSessionIdentity identity) =>
        _identities.TryGetValue(hostId, out var current) && ReferenceEquals(current, identity);

    internal void Replace(int hostId) => _identities[hostId] = BotChannelSessionIdentity.Create();

    internal void Clear(int hostId) => _ = _identities.Remove(hostId);

    internal void Clear(IEnumerable<int> hostIds)
    {
        foreach (var hostId in hostIds)
        {
            _ = _identities.Remove(hostId);
        }
    }
}
