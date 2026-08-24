using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginFeatureDeclaration(
    PluginInstallationIdentity Installation,
    PluginLifecycleFence Fence,
    PluginManifest Manifest
)
{
    public PluginFeatureDescriptor? FindFeature(PluginFeatureId featureId) =>
        Manifest.Features.FirstOrDefault(feature => feature.Id == featureId);

    public PluginSettingDescriptor? FindSetting(PluginSettingId settingId) =>
        Manifest.Settings.FirstOrDefault(setting => setting.Id == settingId);
}

public sealed class PluginFeatureDeclarationSnapshot
{
    internal PluginFeatureDeclarationSnapshot(
        ImmutableDictionary<PluginId, PluginFeatureDeclaration> declarations
    ) => Declarations = declarations;

    public IReadOnlyDictionary<PluginId, PluginFeatureDeclaration> Declarations { get; }

    public static PluginFeatureDeclarationSnapshot Empty { get; } =
        new(ImmutableDictionary<PluginId, PluginFeatureDeclaration>.Empty);
}

public interface IPluginFeatureDeclarationProvider
{
    PluginFeatureDeclarationSnapshot Current { get; }

    PluginDeclarationChangeVersion CurrentVersion { get; }

    ValueTask<PluginDeclarationChangeVersion> WaitForChangeAsync(
        PluginDeclarationChangeVersion observed,
        CancellationToken cancellationToken
    );
}

public sealed record PluginDeclarationChangeVersion(long Value);

public interface IPluginFeatureDeclarationPublisher
{
    void Publish(ValidatedPluginManifest manifest, PluginLifecycleFence fence);

    void Remove(PluginId pluginId, PluginLifecycleFence fence);
}

public sealed class PluginFeatureDeclarationRegistry
    : IPluginFeatureDeclarationProvider,
        IPluginFeatureDeclarationPublisher
{
    private readonly object _sync = new();
    private PluginFeatureDeclarationSnapshot _current = PluginFeatureDeclarationSnapshot.Empty;
    private TaskCompletionSource<PluginDeclarationChangeVersion> _change = NewChangeCompletion();
    private long _version;

    public PluginFeatureDeclarationSnapshot Current => Volatile.Read(ref _current);

    public PluginDeclarationChangeVersion CurrentVersion
    {
        get
        {
            lock (_sync)
            {
                return new(_version);
            }
        }
    }

    public ValueTask<PluginDeclarationChangeVersion> WaitForChangeAsync(
        PluginDeclarationChangeVersion observed,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            return observed.Value != _version
                ? ValueTask.FromResult(new PluginDeclarationChangeVersion(_version))
                : new(_change.Task.WaitAsync(cancellationToken));
        }
    }

    public void Publish(ValidatedPluginManifest manifest, PluginLifecycleFence fence)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(fence);
        var declaration = new PluginFeatureDeclaration(
            new(manifest.Manifest.Id, manifest.Manifest.Release),
            fence,
            manifest.Manifest
        );
        lock (_sync)
        {
            Volatile.Write(
                ref _current,
                new(
                    _current
                        .Declarations.ToImmutableDictionary()
                        .SetItem(manifest.Manifest.Id, declaration)
                )
            );
            NotifyLocked();
        }
    }

    public void Remove(PluginId pluginId, PluginLifecycleFence fence)
    {
        lock (_sync)
        {
            if (
                !_current.Declarations.TryGetValue(pluginId, out var current)
                || current.Fence != fence
            )
            {
                return;
            }

            Volatile.Write(
                ref _current,
                new(_current.Declarations.ToImmutableDictionary().Remove(pluginId))
            );
            NotifyLocked();
        }
    }

    private void NotifyLocked()
    {
        var change = _change;
        _change = NewChangeCompletion();
        _ = change.TrySetResult(new(++_version));
    }

    private static TaskCompletionSource<PluginDeclarationChangeVersion> NewChangeCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public abstract record PluginCoreDependencyStatus
{
    private PluginCoreDependencyStatus() { }

    public sealed record Available : PluginCoreDependencyStatus;

    public sealed record Missing(IReadOnlyList<PluginHostModuleId> Modules)
        : PluginCoreDependencyStatus;
}

public interface IPluginCoreDependencyChecker
{
    PluginCoreDependencyStatus Check(IReadOnlyList<PluginHostModuleRequirement> requirements);
}

public sealed class EmptyPluginCoreDependencyChecker : IPluginCoreDependencyChecker
{
    public PluginCoreDependencyStatus Check(
        IReadOnlyList<PluginHostModuleRequirement> requirements
    ) =>
        requirements.Count == 0
            ? new PluginCoreDependencyStatus.Available()
            : new PluginCoreDependencyStatus.Missing(
                requirements.Select(static requirement => requirement.Id).ToArray()
            );
}
