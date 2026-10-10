using System.Diagnostics;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.CustomCommands;

internal abstract record CustomStoredValueChange
{
    private CustomStoredValueChange(int hostId, int definitionId)
    {
        HostId = hostId;
        DefinitionId = definitionId;
    }

    internal int HostId { get; }
    internal int DefinitionId { get; }

    internal TResult Match<TResult>(
        Func<Definition, TResult> definition,
        Func<Target, TResult> target
    ) =>
        this switch
        {
            Definition value => definition(value),
            Target value => target(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Definition : CustomStoredValueChange
    {
        internal Definition(int hostId, int definitionId)
            : base(hostId, definitionId) { }
    }

    internal sealed record Target : CustomStoredValueChange
    {
        internal Target(int hostId, CustomValueTarget value)
            : base(hostId, value.DefinitionId) => Value = value;

        internal CustomValueTarget Value { get; }
    }
}

internal sealed class CustomStoredValueChanges(
    EventBus<CustomStoredValueChange> events,
    ILogger<CustomStoredValueChanges> log
)
{
    internal IDisposable Subscribe(
        CustomStoredValueChange identity,
        ObserverIdentity observer,
        Func<EventNotification<CustomStoredValueChange>, CancellationToken, ValueTask> handler
    ) => events.Subscribe(identity, observer, handler);

    internal async Task PublishCommittedAsync(IEnumerable<CustomStoredValueChange> identities)
    {
        foreach (var identity in identities.Distinct())
        {
            try
            {
                _ = await events.PublishAsync(identity, CancellationToken.None);
            }
            catch (ObserverFanOutEscalationException exception)
            {
                log.LogError(
                    exception,
                    "Stored-value observer diagnostics failed after commit for host {HostId}, definition {DefinitionId}.",
                    identity.HostId,
                    identity.DefinitionId
                );
            }
        }
    }
}
