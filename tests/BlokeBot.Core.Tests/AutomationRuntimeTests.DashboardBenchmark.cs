using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Dashboards;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Explicit]
    public async Task DashboardControls_Benchmark_ActionAndTargetedStatusPhases()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"benchmark"}""");
        var flow = await f.SaveAsync([source], []);
        var button = DashboardButton(flow, source.Id);
        var timer = DashboardTimer("benchmark");
        var controls = DashboardControls(f);
        var session = DashboardSession(f.HostId);
        var host = new DashboardHostId(f.HostId);
        AutomationDispatchOutcome admitted = new(AutomationDispatchStatus.NoMatchingFlow, []);
        for (var warmup = 0; warmup < 5; warmup++)
        {
            admitted = DashboardDispatched(
                await controls.RunAsync(session, host, button, CancellationToken.None)
            );
            _ = await controls.ReadRunsAsync(
                session,
                host,
                admitted.RunIds,
                CancellationToken.None
            );
            _ = await controls.ApplyCountdownAsync(
                session,
                host,
                timer,
                CountdownOperation.Reset,
                null,
                CancellationToken.None
            );
            _ = await controls.ReadCountdownAsync(session, host, timer, CancellationToken.None);
        }
        List<DashboardBenchmarkPhase> phases = [];
        phases.Add(
            await MeasureDashboardPhase(
                "manual_action_end_to_end",
                async () =>
                {
                    admitted = DashboardDispatched(
                        await controls.RunAsync(session, host, button, CancellationToken.None)
                    );
                    admitted.Status.ShouldBe(AutomationDispatchStatus.Accepted);
                }
            )
        );
        phases.Add(
            await MeasureDashboardPhase(
                "manual_targeted_status",
                async () =>
                {
                    var status = DashboardValue(
                            await controls.ReadRunsAsync(
                                session,
                                host,
                                admitted.RunIds,
                                CancellationToken.None
                            )
                        )
                        .ShouldBeOfType<AutomationRunStatusOutcome.Available>();
                    status.Runs.Length.ShouldBe(1);
                }
            )
        );
        phases.Add(
            await MeasureDashboardPhase(
                "countdown_reset",
                async () =>
                    DashboardCountdown(
                        await controls.ApplyCountdownAsync(
                            session,
                            host,
                            timer,
                            CountdownOperation.Reset,
                            null,
                            CancellationToken.None
                        )
                    )
                        .State.ShouldBe(AutomationCountdownState.Running)
            )
        );
        phases.Add(
            await MeasureDashboardPhase(
                "countdown_current_status",
                async () =>
                    DashboardCountdown(
                        await controls.ReadCountdownAsync(
                            session,
                            host,
                            timer,
                            CancellationToken.None
                        )
                    )
                        .State.ShouldBe(AutomationCountdownState.Running)
            )
        );
        var report = JsonSerializer.Serialize(
            new
            {
                recorded_at = DateTimeOffset.UtcNow,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                processor_count = Environment.ProcessorCount,
                workload = "Shared in-memory SQLite, owner authority, one source-only enabled flow, no external provider, 5 warmups, 100 serial samples per phase; manual action includes real admission and completion, status selects only one returned ID among105 host runs. No UI frames measured.",
                phases,
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(report);
        var destination = Environment.GetEnvironmentVariable("BLOKEBOT_DASHBOARD_BENCHMARK_OUTPUT");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            await File.WriteAllTextAsync(destination, report);
        }
    }

    private static async Task<DashboardBenchmarkPhase> MeasureDashboardPhase(
        string name,
        Func<Task> operation
    )
    {
        var times = new double[100];
        for (var sample = 0; sample < times.Length; sample++)
        {
            var started = Stopwatch.GetTimestamp();
            await operation();
            times[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        Array.Sort(times);
        return new(name, times.Length, times[49], times[94], times[^1]);
    }

    private sealed record DashboardBenchmarkPhase(
        string Name,
        int Samples,
        double P50Ms,
        double P95Ms,
        double MaxMs
    );
}
