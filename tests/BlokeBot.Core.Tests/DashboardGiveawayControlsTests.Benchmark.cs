using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Features.Points.Balances;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGiveawayControlsTests
{
    [Test]
    [Explicit]
    public async Task DashboardGiveaway_Benchmark_CompactReadsAndNativeActionPhases()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedVolumeAsync(300, 1000);
        var read = new Measurement("active_read_1000_entrants");
        var start = new Measurement("start_including_inprocess_stream_liveness");
        var cancel = new Measurement("cancel");
        for (var index = 0; index < 105; index++)
        {
            await read.SampleAsync(
                f,
                index >= 5,
                async () =>
                    Ready(await f.Controls.ReadAsync(f.Owner, f.Host, _configuration, _ct))
                        .ShouldNotBeNull()
                        .EntrantCount.ShouldBe(1000)
            );
        }
        _ = Ready(await f.Controls.CancelAsync(f.Owner, f.Host, _configuration, _ct));
        for (var index = 0; index < 105; index++)
        {
            await start.SampleAsync(
                f,
                index >= 5,
                async () =>
                    _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
                        .ShouldBeOfType<PointOperationOutcome.Succeeded>()
            );
            await cancel.SampleAsync(
                f,
                index >= 5,
                async () =>
                    _ = Ready(await f.Controls.CancelAsync(f.Owner, f.Host, _configuration, _ct))
                        .ShouldBeOfType<PointOperationOutcome.Succeeded>()
            );
        }
        var report = JsonSerializer.Serialize(
            new
            {
                recorded_at = DateTimeOffset.UtcNow,
                hardware = Environment.GetEnvironmentVariable("BLOKEBOT_BENCHMARK_HARDWARE"),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                processor_count = Environment.ProcessorCount,
                workload = "Real dashboard authority/adapter/Points services; isolated file SQLite, two hosts each with300 initial historical rounds and one active round; selected active1000 entrants, other7.5 warmups+100 serial samples per phase. Read returns one header and databaseCOUNT, no entity materialization. Actions run after selected initial roundcancel, with saved300second duration and zero cooldown; no entrants drawn. Recording scheduling boundary/native notifications preserved, worker waits and Automation execution excluded. Start uses inprocess fakeHTTP liveness with no external latency. No browser frames/subframe guarantee/product cap.",
                phases = new[] { read, start, cancel }.Select(x => x.Report()).ToArray(),
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(report);
        var destination = Environment.GetEnvironmentVariable(
            "BLOKEBOT_DASHBOARD_GIVEAWAY_BENCHMARK_OUTPUT"
        );
        if (!string.IsNullOrWhiteSpace(destination))
        {
            await File.WriteAllTextAsync(destination, report);
        }
        var sql = Environment.GetEnvironmentVariable("BLOKEBOT_DASHBOARD_GIVEAWAY_SQL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(sql))
        {
            await File.WriteAllTextAsync(sql, string.Join("\n---\n", read.LastCommands));
        }
    }

    private sealed class Measurement(string phase)
    {
        private readonly List<double> _elapsed = [];
        private readonly List<double> _provider = [];
        private readonly List<int> _commandCounts = [];
        private readonly List<int> _readerOperations = [];
        private readonly Dictionary<string, int> _entities = [];
        internal IReadOnlyList<string> LastCommands { get; private set; } = [];

        internal async Task SampleAsync(Fixture f, bool record, Func<Task> operation)
        {
            f.Queries.Reset();
            var requests = f.Provider.ElapsedMs.Count;
            var started = Stopwatch.GetTimestamp();
            await operation();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!record)
            {
                return;
            }
            _elapsed.Add(elapsed);
            _provider.AddRange(f.Provider.ElapsedMs.Skip(requests));
            _commandCounts.Add(f.Queries.Commands.Count);
            _readerOperations.Add(f.Queries.ReaderOperations.Sum());
            LastCommands = f.Queries.Commands.ToArray();
            foreach (var (type, count) in f.Queries.Entities)
            {
                _entities[type.Name] = _entities.GetValueOrDefault(type.Name) + count;
            }
        }

        internal object Report() =>
            new
            {
                phase,
                samples = _elapsed.Count,
                end_to_end_ms = Percentiles(_elapsed),
                fake_http_provider_ms = Percentiles(_provider),
                provider_requests = _provider.Count,
                sql_reader_commands_min = _commandCounts.Min(),
                sql_reader_commands_max = _commandCounts.Max(),
                sql_reader_operations_min = _readerOperations.Min(),
                sql_reader_operations_max = _readerOperations.Max(),
                materialized_entities_total = _entities,
            };
    }

    private static object Percentiles(IReadOnlyList<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered.Length == 0
            ? new
            {
                p50 = 0d,
                p95 = 0d,
                max = 0d,
            }
            : new
            {
                p50 = ordered[(int)Math.Ceiling(ordered.Length * 0.50) - 1],
                p95 = ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1],
                max = ordered[^1],
            };
    }
}
