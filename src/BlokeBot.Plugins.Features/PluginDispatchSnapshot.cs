using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginCommandRouteKey(PluginHostId HostId, string Route);

public sealed record PluginWebhookRouteKey(
    PluginId PluginId,
    PluginFeatureId FeatureId,
    PluginHostId HostId,
    PluginWebhookId WebhookId
);

public sealed record PluginActionRouteKey(
    PluginId PluginId,
    PluginFeatureId FeatureId,
    PluginHostId HostId,
    PluginActionId ActionId
);

public abstract record PluginDispatchEndpoint
{
    private protected PluginDispatchEndpoint(
        PluginFeatureDeclaration declaration,
        PluginFeatureState state,
        PluginLuaModuleId module,
        PluginHostOperationId operation,
        PluginCallbackRequirements requirements
    )
    {
        Declaration = declaration;
        State = state;
        Module = module;
        Operation = operation;
        Requirements = requirements;
    }

    public PluginFeatureDeclaration Declaration { get; }

    public PluginFeatureState State { get; }

    public PluginLuaModuleId Module { get; }

    public PluginHostOperationId Operation { get; }

    public PluginCallbackRequirements Requirements { get; }

    public sealed record Command : PluginDispatchEndpoint
    {
        internal Command(
            PluginFeatureDeclaration declaration,
            PluginFeatureState state,
            PluginCommandDescriptor descriptor
        )
            : base(
                declaration,
                state,
                descriptor.Module,
                descriptor.Operation,
                descriptor.Requirements
            ) => Descriptor = descriptor;

        public PluginCommandDescriptor Descriptor { get; }
    }

    public sealed record Event : PluginDispatchEndpoint
    {
        internal Event(
            PluginFeatureDeclaration declaration,
            PluginFeatureState state,
            PluginEventHandlerDescriptor descriptor
        )
            : base(
                declaration,
                state,
                descriptor.Module,
                descriptor.Operation,
                descriptor.Requirements
            ) => Descriptor = descriptor;

        public PluginEventHandlerDescriptor Descriptor { get; }
    }

    public sealed record Schedule : PluginDispatchEndpoint
    {
        internal Schedule(
            PluginFeatureDeclaration declaration,
            PluginFeatureState state,
            PluginScheduleHandlerDescriptor descriptor
        )
            : base(
                declaration,
                state,
                descriptor.Module,
                descriptor.Operation,
                descriptor.Requirements
            ) => Descriptor = descriptor;

        public PluginScheduleHandlerDescriptor Descriptor { get; }
    }

    public sealed record Webhook : PluginDispatchEndpoint
    {
        internal Webhook(
            PluginFeatureDeclaration declaration,
            PluginFeatureState state,
            PluginWebhookDescriptor descriptor
        )
            : base(
                declaration,
                state,
                descriptor.Module,
                descriptor.Operation,
                descriptor.Requirements
            ) => Descriptor = descriptor;

        public PluginWebhookDescriptor Descriptor { get; }
    }

    public sealed record Action : PluginDispatchEndpoint
    {
        internal Action(
            PluginFeatureDeclaration declaration,
            PluginFeatureState state,
            PluginActionDescriptor descriptor
        )
            : base(
                declaration,
                state,
                descriptor.Module,
                descriptor.Operation,
                descriptor.Requirements
            ) => Descriptor = descriptor;

        public PluginActionDescriptor Descriptor { get; }
    }
}

public sealed class PluginDispatchSnapshot
{
    internal PluginDispatchSnapshot(
        ImmutableDictionary<PluginCommandRouteKey, PluginDispatchEndpoint.Command> commands,
        ImmutableArray<PluginDispatchEndpoint.Event> events,
        ImmutableArray<PluginDispatchEndpoint.Schedule> schedules,
        ImmutableDictionary<PluginWebhookRouteKey, PluginDispatchEndpoint.Webhook> webhooks,
        ImmutableDictionary<PluginActionRouteKey, PluginDispatchEndpoint.Action> actions
    )
    {
        Commands = commands;
        Events = events;
        Schedules = schedules;
        Webhooks = webhooks;
        Actions = actions;
    }

    public IReadOnlyDictionary<
        PluginCommandRouteKey,
        PluginDispatchEndpoint.Command
    > Commands { get; }

    public ImmutableArray<PluginDispatchEndpoint.Event> Events { get; }

    public ImmutableArray<PluginDispatchEndpoint.Schedule> Schedules { get; }

    public IReadOnlyDictionary<
        PluginWebhookRouteKey,
        PluginDispatchEndpoint.Webhook
    > Webhooks { get; }

    public IReadOnlyDictionary<PluginActionRouteKey, PluginDispatchEndpoint.Action> Actions { get; }

    public static PluginDispatchSnapshot Empty { get; } =
        new(
            ImmutableDictionary<PluginCommandRouteKey, PluginDispatchEndpoint.Command>.Empty,
            [],
            [],
            ImmutableDictionary<PluginWebhookRouteKey, PluginDispatchEndpoint.Webhook>.Empty,
            ImmutableDictionary<PluginActionRouteKey, PluginDispatchEndpoint.Action>.Empty
        );
}

public interface IPluginDispatchSnapshotProvider
{
    PluginDispatchSnapshot Current { get; }
}

public interface IPluginDispatchSnapshotSink
{
    void PublishDeclaration(PluginFeatureDeclaration declaration);

    void RemoveDeclaration(PluginId pluginId, PluginLifecycleFence fence);

    void PublishFeatures(PluginFeatureSnapshot snapshot);
}

public enum PluginCommandActivationRejectionCode
{
    ActivePluginRouteCollision,
}

public abstract record PluginCommandActivationReservationOutcome
{
    private PluginCommandActivationReservationOutcome() { }

    public sealed record Reserved(IAsyncDisposable Reservation)
        : PluginCommandActivationReservationOutcome;

    public sealed record Rejected(PluginCommandActivationRejectionCode Code, string Route)
        : PluginCommandActivationReservationOutcome;
}

public interface IPluginCommandActivationGate
{
    PluginCommandActivationReservationOutcome Reserve(
        PluginFeatureKey key,
        PluginFeatureDescriptor feature
    );
}
