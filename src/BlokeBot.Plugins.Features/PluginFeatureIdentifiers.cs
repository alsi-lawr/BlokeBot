using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed record PluginFeatureKey(
    PluginId PluginId,
    PluginFeatureId FeatureId,
    PluginHostId HostId
);

public sealed record PluginFeatureGeneration
{
    private PluginFeatureGeneration(ulong value) => Value = value;

    public ulong Value { get; }

    public static bool TryCreate(ulong candidate, out PluginFeatureGeneration generation)
    {
        var valid = candidate is > 0 and <= long.MaxValue;
        generation = valid ? new(candidate) : null!;
        return valid;
    }

    internal static bool TryNext(
        PluginFeatureGeneration? current,
        out PluginFeatureGeneration next
    ) => TryCreate((current?.Value ?? 0) + 1, out next);
}

public sealed record PluginFeatureRevision
{
    private PluginFeatureRevision(long value) => Value = value;

    public long Value { get; }

    public static PluginFeatureRevision Initial { get; } = new(0);

    public static bool TryCreate(long candidate, out PluginFeatureRevision revision)
    {
        var valid = candidate >= 0;
        revision = valid ? new(candidate) : null!;
        return valid;
    }

    internal static bool TryNext(PluginFeatureRevision current, out PluginFeatureRevision next) =>
        TryCreate(current.Value == long.MaxValue ? -1 : current.Value + 1, out next);
}

public sealed record PluginConfigurationRevision
{
    private PluginConfigurationRevision(long value) => Value = value;

    public long Value { get; }

    public static PluginConfigurationRevision Initial { get; } = new(0);

    public static bool TryCreate(long candidate, out PluginConfigurationRevision revision)
    {
        var valid = candidate >= 0;
        revision = valid ? new(candidate) : null!;
        return valid;
    }
}
