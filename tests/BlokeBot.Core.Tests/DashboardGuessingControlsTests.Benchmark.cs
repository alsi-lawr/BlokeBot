using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGuessingControlsTests
{
    [Test]
    [Explicit]
    public async Task DashboardGuessing_Benchmark_CompactReadAndNativeLifecyclePayoutPhases()
    {
        await using var f = await Fixture.CreateAsync();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, f.Bound, _ct));
        await f.SeedVolumeAsync(100, 300, 1000);
        var read = new Measurement("current_read_1000_votes");
        var start = new Measurement("native_start");
        var stop = new Measurement("native_stop");
        var declare = new Measurement("native_declare_including_two_winner_payout");
        for (var index = 0; index < 105; index++)
        {
            await read.SampleAsync(
                f,
                index >= 5,
                async () =>
                {
                    var state = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, _ct));
                    _ = state.CurrentRound.ShouldNotBeNull();
                    state.WinnerOptions.Length.ShouldBe(2);
                }
            );
        }
        _ = Ready(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    f.Bound,
                    GuessName.Parse("red"),
                    _ct
                )
            )
            .ShouldBeOfType<GuessingWinnerDeclarationOutcome.Completed>();
        for (var index = 0; index < 105; index++)
        {
            await start.SampleAsync(
                f,
                index >= 5,
                async () =>
                    _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, f.Bound, _ct))
                        .ShouldBeOfType<GuessingOperationOutcome.Succeeded>()
            );
            await f.WinningVotesAsync();
            await stop.SampleAsync(
                f,
                index >= 5,
                async () =>
                    _ = Ready(await f.Controls.StopAsync(f.Owner, f.Host, f.Bound, _ct))
                        .ShouldBeOfType<GuessingOperationOutcome.Succeeded>()
            );
            await declare.SampleAsync(
                f,
                index >= 5,
                async () =>
                    _ = Ready(
                            await f.Controls.DeclareWinnerAsync(
                                f.Owner,
                                f.Host,
                                f.Bound,
                                GuessName.Parse("blue"),
                                _ct
                            )
                        )
                        .ShouldBeOfType<GuessingWinnerDeclarationOutcome.Completed>()
                        .Result.ShouldBeOfType<GuessingOperationOutcome.Succeeded>()
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
                workload = "Actual dashboard authority/adapter/native GuessingRoundService and PointBalanceService; isolated file SQLite; two hosts each100 unrelated profiles/300 historical rounds. Compact read one current header/selected descriptor/two canonical options despite1000 votes; no vote/profiles/history entities.5warmups+100 serial samples per phase. Lifecycle start→two actual RecordGuess calls (outside measured phases)→stop→declare; declaration includes native transaction/two25-point credits/ledgers and existing events/presenter, no external provider/chat queue/automation or UI frame measurement. Presenter records intent only. Finite fixture sizes are not product caps.",
                phases = new[] { read, start, stop, declare }.Select(x => x.Report()).ToArray(),
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(report);
        var output = Environment.GetEnvironmentVariable(
            "BLOKEBOT_DASHBOARD_GUESSING_BENCHMARK_OUTPUT"
        );
        if (!string.IsNullOrWhiteSpace(output))
        {
            await File.WriteAllTextAsync(output, report);
        }
        var sql = Environment.GetEnvironmentVariable("BLOKEBOT_DASHBOARD_GUESSING_SQL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(sql))
        {
            await File.WriteAllTextAsync(sql, string.Join("\n---\n", read.LastCommands));
        }
    }

    private sealed class Measurement(string phase)
    {
        private readonly List<double> _elapsed = [];
        private readonly List<int> _commands = [];
        private readonly List<int> _reads = [];
        private readonly Dictionary<string, int> _entities = [];
        internal IReadOnlyList<string> LastCommands { get; private set; } = [];

        internal async Task SampleAsync(Fixture f, bool record, Func<Task> operation)
        {
            f.Queries.Reset();
            var started = Stopwatch.GetTimestamp();
            await operation();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!record)
            {
                return;
            }
            _elapsed.Add(elapsed);
            _commands.Add(f.Queries.Commands.Count);
            _reads.Add(f.Queries.ReaderOperations.Sum());
            LastCommands = f.Queries.Commands.ToArray();
            foreach (var (type, count) in f.Queries.Entities)
            {
                _entities[type.Name] = _entities.GetValueOrDefault(type.Name) + count;
            }
        }

        internal object Report()
        {
            var values = _elapsed.Order().ToArray();
            return new
            {
                phase,
                samples = values.Length,
                end_to_end_ms = new
                {
                    p50 = values[49],
                    p95 = values[94],
                    max = values[^1],
                },
                sql_reader_commands_min = _commands.Min(),
                sql_reader_commands_max = _commands.Max(),
                sql_reader_operations_min = _reads.Min(),
                sql_reader_operations_max = _reads.Max(),
                materialized_entities_total = _entities,
            };
        }
    }
}
