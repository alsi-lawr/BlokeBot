using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginCommandRouteKey(PluginHostId HostId, string Route);

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
}

public sealed class PluginDispatchSnapshot
{
    internal PluginDispatchSnapshot(
        ImmutableDictionary<PluginCommandRouteKey, PluginDispatchEndpoint.Command> commands,
        ImmutableArray<PluginDispatchEndpoint.Event> events,
        ImmutableArray<PluginDispatchEndpoint.Schedule> schedules
    )
    {
        Commands = commands;
        Events = events;
        Schedules = schedules;
    }

    public IReadOnlyDictionary<
        PluginCommandRouteKey,
        PluginDispatchEndpoint.Command
    > Commands { get; }

    public ImmutableArray<PluginDispatchEndpoint.Event> Events { get; }

    public ImmutableArray<PluginDispatchEndpoint.Schedule> Schedules { get; }

    public static PluginDispatchSnapshot Empty { get; } =
        new(
            ImmutableDictionary<PluginCommandRouteKey, PluginDispatchEndpoint.Command>.Empty,
            [],
            []
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
