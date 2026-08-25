using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginPageSessionBinding(
    PluginInstallationIdentity Installation,
    PluginFeatureKey Feature,
    PluginLifecycleFence Fence,
    PluginFeatureGeneration Generation,
    PluginPageId PageId
)
{
    public static PluginPageSessionBinding From(PluginPageEndpoint endpoint) =>
        new(
            endpoint.Definition.Declaration.Installation,
            endpoint.State.Key,
            endpoint.State.Fence,
            endpoint.State.Generation,
            endpoint.Definition.Id
        );
}

public sealed record PluginPageSession(
    PluginPageSessionId Id,
    PluginPageSessionBinding Binding,
    ImmutableHashSet<string> MessageOrigins,
    DateTimeOffset ExpiresAtUtc
);

public abstract record PluginPageSessionCreation
{
    private PluginPageSessionCreation() { }

    public sealed record Created(PluginPageSession Session) : PluginPageSessionCreation;

    public sealed record CapacityReached : PluginPageSessionCreation;
}

public enum PluginPageMessageRejectionCode
{
    Missing,
    Expired,
    Stale,
    InvalidOrigin,
    Duplicate,
    MessageLimitReached,
}

public abstract record PluginPageMessageAdmission
{
    private PluginPageMessageAdmission() { }

    public sealed record Admitted(PluginPageSession Session) : PluginPageMessageAdmission;

    public sealed record Rejected(PluginPageMessageRejectionCode Code) : PluginPageMessageAdmission;
}

public sealed class PluginPageSessionRegistry
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(15);
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly Dictionary<PluginPageSessionId, SessionState> _sessions = [];

    public PluginPageSessionRegistry(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public PluginPageSessionCreation Create(
        PluginPageEndpoint endpoint,
        IEnumerable<string> messageOrigins
    )
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(messageOrigins);
        lock (_sync)
        {
            RemoveExpiredLocked();
            if (_sessions.Count >= PluginContractLimits.MaximumPageSessions)
            {
                return new PluginPageSessionCreation.CapacityReached();
            }

            _ = PluginPageSessionId.TryCreate(Guid.NewGuid(), out var id);
            var session = new PluginPageSession(
                id,
                PluginPageSessionBinding.From(endpoint),
                messageOrigins.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                _timeProvider.GetUtcNow().Add(_lifetime)
            );
            _sessions.Add(id, new(session));
            return new PluginPageSessionCreation.Created(session);
        }
    }

    public PluginPageMessageAdmission AdmitMessage(
        PluginPageSessionId sessionId,
        PluginPageMessageId messageId,
        PluginPageSessionBinding expected,
        string origin
    )
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
            {
                return Rejected(PluginPageMessageRejectionCode.Missing);
            }
            if (state.Session.ExpiresAtUtc <= _timeProvider.GetUtcNow())
            {
                _ = _sessions.Remove(sessionId);
                return Rejected(PluginPageMessageRejectionCode.Expired);
            }
            if (state.Session.Binding != expected)
            {
                return Rejected(PluginPageMessageRejectionCode.Stale);
            }
            if (!state.Session.MessageOrigins.Contains(origin))
            {
                return Rejected(PluginPageMessageRejectionCode.InvalidOrigin);
            }
            if (state.MessageIds.Contains(messageId))
            {
                return Rejected(PluginPageMessageRejectionCode.Duplicate);
            }
            if (state.MessageIds.Count >= PluginContractLimits.MaximumPageMessagesPerSession)
            {
                return Rejected(PluginPageMessageRejectionCode.MessageLimitReached);
            }

            _ = state.MessageIds.Add(messageId);
            return new PluginPageMessageAdmission.Admitted(state.Session);
        }
    }

    public void Remove(PluginPageSessionId sessionId)
    {
        lock (_sync)
        {
            _ = _sessions.Remove(sessionId);
        }
    }

    private void RemoveExpiredLocked()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (
            var id in _sessions
                .Where(pair => pair.Value.Session.ExpiresAtUtc <= now)
                .Select(static pair => pair.Key)
                .ToArray()
        )
        {
            _ = _sessions.Remove(id);
        }
    }

    private static PluginPageMessageAdmission.Rejected Rejected(
        PluginPageMessageRejectionCode code
    ) => new(code);

    private sealed record SessionState(PluginPageSession Session)
    {
        internal HashSet<PluginPageMessageId> MessageIds { get; } = [];
    }
}
