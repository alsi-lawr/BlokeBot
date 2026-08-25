using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed class PluginDispatchSnapshotRegistry
    : IPluginDispatchSnapshotProvider,
        IPluginDispatchSnapshotSink,
        IPluginCommandActivationGate
{
    private readonly IPluginRuntimeSnapshotProvider? _runtime;
    private readonly object _sync = new();
    private readonly Dictionary<PluginId, PluginFeatureDeclaration> _declarations = [];
    private readonly HashSet<PluginCommandRouteKey> _reservations = [];
    private PluginFeatureSnapshot _features = PluginFeatureSnapshot.Empty;
    private PluginRuntimeSnapshot? _observedRuntime;
    private PluginDispatchSnapshot _current = PluginDispatchSnapshot.Empty;

    public PluginDispatchSnapshotRegistry(IPluginRuntimeSnapshotProvider? runtime = null) =>
        _runtime = runtime;

    public PluginDispatchSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                RefreshRuntimeLocked();
                return _current;
            }
        }
    }

    public void PublishDeclaration(PluginFeatureDeclaration declaration)
    {
        lock (_sync)
        {
            _declarations[declaration.Installation.PluginId] = declaration;
            RebuildLocked();
        }
    }

    public void RemoveDeclaration(PluginId pluginId, PluginLifecycleFence fence)
    {
        lock (_sync)
        {
            if (
                _declarations.TryGetValue(pluginId, out var declaration)
                && declaration.Fence == fence
            )
            {
                _ = _declarations.Remove(pluginId);
                RebuildLocked();
            }
        }
    }

    public void PublishFeatures(PluginFeatureSnapshot snapshot)
    {
        lock (_sync)
        {
            _features = snapshot;
            RebuildLocked();
        }
    }

    public PluginCommandActivationReservationOutcome Reserve(
        PluginFeatureKey key,
        PluginFeatureDescriptor feature
    )
    {
        lock (_sync)
        {
            RefreshRuntimeLocked();
            var routes = feature
                .DispatchDeclarations.Commands.Select(command => new PluginCommandRouteKey(
                    key.HostId,
                    command.Route
                ))
                .ToArray();
            var collision = routes.FirstOrDefault(route =>
                _reservations.Contains(route)
                || (_current.Commands.TryGetValue(route, out var active) && active.State.Key != key)
            );
            if (collision is not null)
            {
                return new PluginCommandActivationReservationOutcome.Rejected(
                    PluginCommandActivationRejectionCode.ActivePluginRouteCollision,
                    collision.Route
                );
            }

            foreach (var route in routes)
            {
                _ = _reservations.Add(route);
            }
            return new PluginCommandActivationReservationOutcome.Reserved(
                new Reservation(this, routes)
            );
        }
    }

    private void Release(IReadOnlyList<PluginCommandRouteKey> routes)
    {
        lock (_sync)
        {
            foreach (var route in routes)
            {
                _ = _reservations.Remove(route);
            }
        }
    }

    private void RebuildLocked()
    {
        _observedRuntime = _runtime?.Current;
        var runtimeEntries = _observedRuntime?.Entries;
        var priorOwners = _current.Commands.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.State.Key
        );
        var commands = ImmutableDictionary.CreateBuilder<
            PluginCommandRouteKey,
            PluginDispatchEndpoint.Command
        >();
        var events = ImmutableArray.CreateBuilder<PluginDispatchEndpoint.Event>();
        var schedules = ImmutableArray.CreateBuilder<PluginDispatchEndpoint.Schedule>();
        var webhooks = ImmutableDictionary.CreateBuilder<
            PluginWebhookRouteKey,
            PluginDispatchEndpoint.Webhook
        >();
        var actions = ImmutableDictionary.CreateBuilder<
            PluginActionRouteKey,
            PluginDispatchEndpoint.Action
        >();
        var eligible = _features
            .States.Values.Where(static state => state.Enabled)
            .Select(EndpointSource)
            .Where(static source => source is not null)
            .Select(static source => source!)
            .ToArray();

        foreach (var source in eligible)
        {
            foreach (var descriptor in source.Feature.DispatchDeclarations.Events)
            {
                events.Add(new(source.Declaration, source.State, descriptor));
            }
            foreach (var descriptor in source.Feature.DispatchDeclarations.Schedules)
            {
                schedules.Add(new(source.Declaration, source.State, descriptor));
            }
            foreach (var descriptor in source.Feature.DispatchDeclarations.Webhooks)
            {
                webhooks[
                    new(
                        source.State.Key.PluginId,
                        source.State.Key.FeatureId,
                        source.State.Key.HostId,
                        descriptor.Id
                    )
                ] = new(source.Declaration, source.State, descriptor);
            }
            foreach (var descriptor in source.Feature.DispatchDeclarations.Actions)
            {
                actions[
                    new(
                        source.State.Key.PluginId,
                        source.State.Key.FeatureId,
                        source.State.Key.HostId,
                        descriptor.Id
                    )
                ] = new(source.Declaration, source.State, descriptor);
            }
        }

        foreach (
            var group in eligible
                .SelectMany(source =>
                    source.Feature.DispatchDeclarations.Commands.Select(
                        descriptor => new CommandCandidate(
                            new(source.State.Key.HostId, descriptor.Route),
                            source,
                            descriptor
                        )
                    )
                )
                .GroupBy(static candidate => candidate.Route)
        )
        {
            var selected = priorOwners.TryGetValue(group.Key, out var priorOwner)
                ? group.FirstOrDefault(candidate => candidate.Source.State.Key == priorOwner)
                : null;
            selected ??= group
                .OrderBy(static candidate => candidate.Source.State.Key.PluginId.Value)
                .ThenBy(static candidate => candidate.Source.State.Key.FeatureId.Value)
                .First();
            commands[group.Key] = new(
                selected.Source.Declaration,
                selected.Source.State,
                selected.Descriptor
            );
        }

        Volatile.Write(
            ref _current,
            new(
                commands.ToImmutable(),
                events.ToImmutable(),
                schedules.ToImmutable(),
                webhooks.ToImmutable(),
                actions.ToImmutable()
            )
        );

        EndpointSource? EndpointSource(PluginFeatureState state) =>
            (
                !RuntimeIsCurrent(state)
                || !_declarations.TryGetValue(state.Key.PluginId, out var declaration)
                || declaration.Fence != state.Fence
                || declaration.FindFeature(state.Key.FeatureId) is not { } feature
            )
                ? null
                : new(declaration, state, feature);

        bool RuntimeIsCurrent(PluginFeatureState state) =>
            runtimeEntries is null
            || (
                runtimeEntries.TryGetValue(state.Key.PluginId, out var runtime)
                && runtime.Fence == state.Fence
                && runtime.Phase == PluginLifecyclePhase.Active
                && runtime.WorkerMode == PluginWorkerMode.Admitted
            );
    }

    private void RefreshRuntimeLocked()
    {
        if (_runtime is not null && !ReferenceEquals(_observedRuntime, _runtime.Current))
        {
            RebuildLocked();
        }
    }

    private sealed record EndpointSource(
        PluginFeatureDeclaration Declaration,
        PluginFeatureState State,
        PluginFeatureDescriptor Feature
    );

    private sealed record CommandCandidate(
        PluginCommandRouteKey Route,
        EndpointSource Source,
        PluginCommandDescriptor Descriptor
    );

    private sealed class Reservation(
        PluginDispatchSnapshotRegistry owner,
        IReadOnlyList<PluginCommandRouteKey> routes
    ) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(routes);
            }
            return ValueTask.CompletedTask;
        }
    }
}
