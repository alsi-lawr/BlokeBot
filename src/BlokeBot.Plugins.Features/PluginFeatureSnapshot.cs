using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginFeatureFence(
    PluginLifecycleFence Lifecycle,
    PluginFeatureGeneration FeatureGeneration
);

public sealed class PluginFeatureSnapshot
{
    internal PluginFeatureSnapshot(
        ImmutableDictionary<PluginFeatureKey, PluginFeatureState> states
    ) => States = states;

    public IReadOnlyDictionary<PluginFeatureKey, PluginFeatureState> States { get; }

    public static PluginFeatureSnapshot Empty { get; } =
        new(ImmutableDictionary<PluginFeatureKey, PluginFeatureState>.Empty);
}

public sealed record PluginFeatureChangeVersion(long Value);

public interface IPluginFeatureSnapshotProvider
{
    PluginFeatureSnapshot Current { get; }

    ValueTask<PluginFeatureChangeVersion> WaitForChangeAsync(
        PluginFeatureChangeVersion observed,
        CancellationToken cancellationToken
    );

    PluginFeatureChangeVersion CurrentVersion { get; }
}

public sealed class PluginFeatureSnapshotRegistry : IPluginFeatureSnapshotProvider
{
    private readonly object _sync = new();
    private PluginFeatureSnapshot _current = PluginFeatureSnapshot.Empty;
    private TaskCompletionSource<PluginFeatureChangeVersion> _change = NewChangeCompletion();
    private long _version;

    public PluginFeatureSnapshot Current => Volatile.Read(ref _current);

    public PluginFeatureChangeVersion CurrentVersion
    {
        get
        {
            lock (_sync)
            {
                return new(_version);
            }
        }
    }

    public ValueTask<PluginFeatureChangeVersion> WaitForChangeAsync(
        PluginFeatureChangeVersion observed,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            return observed.Value != _version
                ? ValueTask.FromResult(new PluginFeatureChangeVersion(_version))
                : new(_change.Task.WaitAsync(cancellationToken));
        }
    }

    public void Hydrate(IEnumerable<PluginFeatureState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        lock (_sync)
        {
            var merged = _current.States.ToImmutableDictionary();
            foreach (var state in states)
            {
                if (
                    !merged.TryGetValue(state.Key, out var current)
                    || state.Revision.Value > current.Revision.Value
                )
                {
                    merged = merged.SetItem(state.Key, state);
                }
            }
            PublishLocked(merged);
        }
    }

    public void Publish(PluginFeatureState state)
    {
        lock (_sync)
        {
            if (
                _current.States.TryGetValue(state.Key, out var current)
                && current.Revision.Value >= state.Revision.Value
            )
            {
                return;
            }
            var states = _current.States.ToImmutableDictionary().SetItem(state.Key, state);
            PublishLocked(states);
        }
    }

    public void Remove(PluginId pluginId)
    {
        lock (_sync)
        {
            var states = _current
                .States.Where(pair => pair.Key.PluginId != pluginId)
                .ToImmutableDictionary();
            PublishLocked(states);
        }
    }

    public bool IsCurrent(
        PluginFeatureState state,
        PluginFeatureReadinessDependency readinessDependency
    )
    {
        var snapshot = Current;
        return snapshot.States.TryGetValue(state.Key, out var current)
            && current.Fence == state.Fence
            && current.Generation == state.Generation
            && current.Enabled
            && (
                readinessDependency == PluginFeatureReadinessDependency.Independent
                || (
                    current.Revision == state.Revision
                    && current.Readiness is PluginFeatureReadiness.Ready
                )
            );
    }

    private void PublishLocked(ImmutableDictionary<PluginFeatureKey, PluginFeatureState> states)
    {
        Volatile.Write(ref _current, new(states));
        var change = _change;
        var version = new PluginFeatureChangeVersion(++_version);
        _change = NewChangeCompletion();
        _ = change.TrySetResult(version);
    }

    private static TaskCompletionSource<PluginFeatureChangeVersion> NewChangeCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
