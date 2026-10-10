using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardNativeTwitchControlsTests
{
    [Test]
    [Explicit]
    public async Task DashboardNative_Benchmark_ActionsAndCompactReadProviderPhases()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedHistoryAsync(150);
        var pollStart = new Measurement("poll_start");
        var pollEnd = new Measurement("poll_end");
        var predictionStart = new Measurement("prediction_start_including_eligibility");
        var predictionCancel = new Measurement("prediction_cancel");
        var pollRead = new Measurement("poll_current_read");
        var predictionRead = new Measurement("prediction_current_read_including_eligibility");
        for (var index = 0; index < 105; index++)
        {
            var record = index >= 5;
            await pollStart.SampleAsync(
                f,
                record,
                async () =>
                    _ = Value(
                            await f.PollControls.StartAsync(
                                f.Owner,
                                f.Host,
                                f.PollConfiguration,
                                _ct
                            )
                        )
                        .ShouldBeOfType<PollOperationOutcome.Started>()
            );
            await pollEnd.SampleAsync(
                f,
                record,
                async () =>
                    _ = Value(
                            await f.PollControls.EndAsync(
                                f.Owner,
                                f.Host,
                                f.PollConfiguration,
                                false,
                                _ct
                            )
                        )
                        .ShouldBeOfType<PollOperationOutcome.Ended>()
            );
            await predictionStart.SampleAsync(
                f,
                record,
                async () =>
                    _ = Value(
                            await f.PredictionControls.StartAsync(
                                f.Owner,
                                f.Host,
                                f.PredictionConfiguration,
                                _ct
                            )
                        )
                        .ShouldBeOfType<PredictionOperationOutcome.Started>()
            );
            await predictionCancel.SampleAsync(
                f,
                record,
                async () =>
                    _ = Value(
                            await f.PredictionControls.CancelAsync(
                                f.Owner,
                                f.Host,
                                f.PredictionConfiguration,
                                true,
                                _ct
                            )
                        )
                        .ShouldBeOfType<PredictionOperationOutcome.Updated>()
            );
        }
        _ = await f.PollControls.StartAsync(f.Owner, f.Host, f.PollConfiguration, _ct);
        _ = await f.PredictionControls.StartAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct);
        for (var index = 0; index < 105; index++)
        {
            var record = index >= 5;
            await pollRead.SampleAsync(
                f,
                record,
                async () =>
                {
                    _ = Value(
                        await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, _ct)
                    )
                        .ActivePoll.ShouldNotBeNull();
                    f.Queries.Materialized<TwitchPollTemplate>().ShouldBe(1);
                    f.Queries.Materialized<TwitchPoll>().ShouldBe(1);
                }
            );
            await predictionRead.SampleAsync(
                f,
                record,
                async () =>
                {
                    _ = Value(
                        await f.PredictionControls.ReadAsync(
                            f.Owner,
                            f.Host,
                            f.PredictionConfiguration,
                            _ct
                        )
                    )
                        .ActivePrediction.ShouldNotBeNull();
                    f.Queries.Materialized<TwitchPredictionTemplate>().ShouldBe(1);
                    f.Queries.Materialized<TwitchPrediction>().ShouldBe(0);
                }
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
                workload = "Real dashboard adapters/native services, shared-memory SQLite, two hosts, initial150 unrelated templates and150 historical results per host per family plus two selected templates;5 warmups+100 serial samples per phase. Native end/cancel history trimming remains unchanged (100 retained current-host results); compact reads return one selected template and one current row, never history. Owner authority; fake in-process HTTP with no external network latency. No UI/browser frames measured; fixtures are not product caps.",
                phases = new[]
                {
                    pollStart,
                    pollEnd,
                    predictionStart,
                    predictionCancel,
                    pollRead,
                    predictionRead,
                }
                    .Select(x => x.Report())
                    .ToArray(),
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(report);
        var destination = Environment.GetEnvironmentVariable(
            "BLOKEBOT_DASHBOARD_NATIVE_BENCHMARK_OUTPUT"
        );
        if (!string.IsNullOrWhiteSpace(destination))
        {
            await File.WriteAllTextAsync(destination, report);
        }
        var sqlDestination = Environment.GetEnvironmentVariable(
            "BLOKEBOT_DASHBOARD_NATIVE_SQL_OUTPUT"
        );
        if (!string.IsNullOrWhiteSpace(sqlDestination))
        {
            await File.WriteAllTextAsync(
                sqlDestination,
                "POLL COMPACT READ (last measured sample):\n"
                    + string.Join("\n---\n", pollRead.LastCommands)
                    + "\nPREDICTION COMPACT READ (last measured sample):\n"
                    + string.Join("\n---\n", predictionRead.LastCommands)
            );
        }
    }

    private sealed class Measurement(string phase)
    {
        private readonly List<double> _elapsed = [];
        private readonly List<double> _providerElapsed = [];
        private readonly List<int> _queryCounts = [];
        private int _eligibilityRequests;
        private int _mutationRequests;
        private readonly Dictionary<string, int> _materialized = [];
        internal IReadOnlyList<string> LastCommands { get; private set; } = [];

        internal async Task SampleAsync(Fixture fixture, bool record, Func<Task> operation)
        {
            fixture.Queries.Reset();
            var requests = fixture.Provider.Requests.Count;
            var start = Stopwatch.GetTimestamp();
            await operation();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (!record)
            {
                return;
            }
            _elapsed.Add(elapsed);
            _queryCounts.Add(fixture.Queries.Commands.Count);
            LastCommands = fixture.Queries.Commands.ToArray();
            foreach (var (type, count) in fixture.Queries.Entities)
            {
                _materialized[type.Name] = _materialized.GetValueOrDefault(type.Name) + count;
            }
            foreach (var request in fixture.Provider.Requests.Skip(requests))
            {
                _providerElapsed.Add(request.ElapsedMs);
                if (request.Path.EndsWith("/users", StringComparison.Ordinal))
                {
                    _eligibilityRequests++;
                }
                else
                {
                    _mutationRequests++;
                }
            }
        }

        internal object Report() =>
            new
            {
                phase,
                samples = _elapsed.Count,
                end_to_end_ms = Percentiles(_elapsed),
                fake_http_provider_ms = Percentiles(_providerElapsed),
                eligibility_requests = _eligibilityRequests,
                mutation_requests = _mutationRequests,
                sql_commands_min = _queryCounts.Min(),
                sql_commands_max = _queryCounts.Max(),
                materialized_entities_total = _materialized,
            };
    }

    private static object Percentiles(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0
            ? new
            {
                p50 = 0d,
                p95 = 0d,
                max = 0d,
            }
            : new
            {
                p50 = sorted[(int)Math.Ceiling(sorted.Length * 0.50) - 1],
                p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1],
                max = sorted[^1],
            };
    }
}
