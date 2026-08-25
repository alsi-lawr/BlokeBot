using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginScheduleFileStore : IPluginScheduleStore, IDisposable
{
    private static readonly JsonSerializerOptions _json = CreateJsonOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private Dictionary<Guid, ScheduleDocument>? _entries;

    public PluginScheduleFileStore(IOptions<BlokeBotOptions> options)
    {
        var databasePath = Path.GetFullPath(options.Value.DatabasePath);
        var stateDirectory = Path.GetDirectoryName(databasePath) ?? Environment.CurrentDirectory;
        _path = Path.Combine(stateDirectory, "plugin-schedules.json");
    }

    internal PluginScheduleFileStore(string path) => _path = Path.GetFullPath(path);

    public async ValueTask<IReadOnlyList<PluginScheduleEntry>> LoadAsync(
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _entries!.Values.Select(static entry => entry.ToDomain()).ToArray();
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public ValueTask UpsertAsync(PluginScheduleEntry entry, CancellationToken cancellationToken) =>
        MutateAsync(
            entries =>
            {
                entries[entry.Id] = ScheduleDocument.From(entry);
                return true;
            },
            cancellationToken
        );

    public async ValueTask<bool> TryConsumeOccurrenceAsync(
        PluginScheduleEntry observed,
        DateTimeOffset? nextDueAtUtc,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (
                !_entries!.TryGetValue(observed.Id, out var persisted)
                || !persisted.MatchesOccurrence(observed)
            )
            {
                return false;
            }

            if (nextDueAtUtc is { } next)
            {
                _entries[observed.Id] = ScheduleDocument.From(observed with { DueAtUtc = next });
            }
            else
            {
                _ = _entries.Remove(observed.Id);
            }
            await WriteAsync(cancellationToken);
            return true;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public ValueTask RemoveAsync(Guid scheduleId, CancellationToken cancellationToken) =>
        MutateAsync(entries => entries.Remove(scheduleId), cancellationToken);

    public ValueTask RemoveFeatureAsync(
        PluginFeatureKey feature,
        PluginFeatureFence fence,
        CancellationToken cancellationToken
    ) =>
        MutateAsync(
            entries => RemoveWhere(entries, entry => entry.Matches(feature, fence)),
            cancellationToken
        );

    public ValueTask RemovePluginAsync(PluginId pluginId, CancellationToken cancellationToken) =>
        MutateAsync(
            entries => RemoveWhere(entries, entry => entry.PluginId == pluginId.Value),
            cancellationToken
        );

    public void Dispose() => _gate.Dispose();

    private async ValueTask MutateAsync(
        Func<Dictionary<Guid, ScheduleDocument>, bool> mutate,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (mutate(_entries!))
            {
                await WriteAsync(cancellationToken);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async ValueTask EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null)
        {
            return;
        }
        if (!File.Exists(_path))
        {
            _entries = [];
            return;
        }
        await using var stream = File.OpenRead(_path);
        var document = await JsonSerializer.DeserializeAsync<ScheduleStoreDocument>(
            stream,
            _json,
            cancellationToken
        );
        _entries = (
            document ?? throw new InvalidDataException("Plugin schedule state is empty.")
        ).Schedules.ToDictionary(static entry => entry.Id);
    }

    private async ValueTask WriteAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new ScheduleStoreDocument(
                        1,
                        [.. _entries!.Values.OrderBy(static entry => entry.Id)]
                    ),
                    _json,
                    cancellationToken
                );
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool RemoveWhere(
        Dictionary<Guid, ScheduleDocument> entries,
        Func<ScheduleDocument, bool> predicate
    )
    {
        var removed = false;
        foreach (
            var key in entries.Values.Where(predicate).Select(static entry => entry.Id).ToArray()
        )
        {
            removed |= entries.Remove(key);
        }
        return removed;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record ScheduleStoreDocument(
        int Version,
        ImmutableArray<ScheduleDocument> Schedules
    );

    private sealed record ScheduleDocument(
        Guid Id,
        string PluginId,
        string FeatureId,
        int HostId,
        Guid OperationId,
        ulong WorkerGeneration,
        ulong FeatureGeneration,
        string HandlerId,
        DateTimeOffset DueAtUtc,
        long? IntervalSeconds,
        PluginValue.Map Input
    )
    {
        internal static ScheduleDocument From(PluginScheduleEntry entry) =>
            new(
                entry.Id,
                entry.Feature.PluginId.Value,
                entry.Feature.FeatureId.Value,
                entry.Feature.HostId.Value,
                entry.Fence.Lifecycle.OperationId.Value,
                entry.Fence.Lifecycle.Generation.Value,
                entry.Fence.FeatureGeneration.Value,
                entry.HandlerId.Value,
                entry.DueAtUtc,
                entry.IntervalSeconds,
                entry.Input
            );

        internal bool Matches(PluginFeatureKey feature, PluginFeatureFence fence) =>
            PluginId == feature.PluginId.Value
            && FeatureId == feature.FeatureId.Value
            && HostId == feature.HostId.Value
            && OperationId == fence.Lifecycle.OperationId.Value
            && WorkerGeneration == fence.Lifecycle.Generation.Value
            && FeatureGeneration == fence.FeatureGeneration.Value;

        internal bool MatchesOccurrence(PluginScheduleEntry observed) =>
            Id == observed.Id
            && Matches(observed.Feature, observed.Fence)
            && HandlerId == observed.HandlerId.Value
            && DueAtUtc == observed.DueAtUtc
            && IntervalSeconds == observed.IntervalSeconds
            && PluginValueComparer.SemanticallyEquals(Input, observed.Input);

        internal PluginScheduleEntry ToDomain() =>
            (
                !BlokeBot.Plugins.Contracts.PluginId.TryCreate(PluginId, out var pluginId)
                || !PluginFeatureId.TryCreate(FeatureId, out var featureId)
                || !PluginHostId.TryCreate(HostId, out var hostId)
                || !PluginLifecycleOperationId.TryCreate(OperationId, out var operationId)
                || !PluginWorkerGeneration.TryCreate(WorkerGeneration, out var workerGeneration)
                || !PluginFeatureGeneration.TryCreate(FeatureGeneration, out var featureGeneration)
                || !PluginScheduleHandlerId.TryCreate(HandlerId, out var handlerId)
            )
                ? throw new InvalidDataException(
                    "Plugin schedule state contains an invalid identity."
                )
                : new(
                    Id,
                    new(pluginId, featureId, hostId),
                    new(new(operationId, workerGeneration), featureGeneration),
                    handlerId,
                    DueAtUtc,
                    IntervalSeconds,
                    Input
                );
    }
}
