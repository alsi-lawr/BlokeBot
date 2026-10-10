using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    [Test, Explicit]
    public async Task DashboardValues_Benchmark_CommittedNotificationToSessionProjection()
    {
        await using var fixture = await Fixture.CreateAsync(
            await PointProviderFixture.SqliteFileAsync()
        );
        var changes = StoredValueTestChanges.Create();
        var values = new CustomStoredValueService(fixture.Database, changes);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            fixture.Host.Value,
            "Measured",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        long committedAt = 0;
        List<double> samples = [];
        using var start = changes.Subscribe(
            new CustomStoredValueChange.Target(fixture.Host.Value, new(definition.Id, "", "")),
            BlokeBot.Eventing.ObserverIdentity.Named("DashboardValues.Measurement"),
            (_, _) =>
            {
                committedAt = Stopwatch.GetTimestamp();
                return ValueTask.CompletedTask;
            }
        );
        using var display = ValueReader(fixture, changes)
            .CreateSession(() =>
            {
                if (committedAt != 0)
                {
                    samples.Add(Stopwatch.GetElapsedTime(committedAt).TotalMilliseconds);
                }
                return ValueTask.CompletedTask;
            });
        await display.BindAsync(fixture.Owner, fixture.Host, ValueConfiguration(definition), _ct);
        for (var sample = 0; sample < 105; sample++)
        {
            (
                await values.SaveValueAsync(
                    fixture.Host.Value,
                    (await DisplayAsync(display)).Value,
                    CustomValueKind.Number,
                    sample.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ct
                )
            ).Status.ShouldBe(CustomValueEditStatus.Saved);
            (await DisplayAsync(display)).Value.Value.ShouldBe(
                sample.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
        }
        var measured = samples.Skip(5).Order().ToArray();
        var report = JsonSerializer.Serialize(
            new
            {
                recorded_at = DateTimeOffset.UtcNow,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                processor_count = Environment.ProcessorCount,
                workload = "File SQLite, owner authority, one bound global scalar, 5 warmups and 100 serial actual Variables commits. Timing starts at the first committed target observer and ends after the session's targeted native projection callback. Excludes write/commit and browser presentation. No idle-machine guarantee or product count cap.",
                samples = measured.Length,
                p50_ms = measured[(int)Math.Ceiling(measured.Length * 0.5) - 1],
                p95_ms = measured[(int)Math.Ceiling(measured.Length * 0.95) - 1],
                max_ms = measured[^1],
                raw_ms = samples.Skip(5).ToArray(),
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(report);
        var destination = Environment.GetEnvironmentVariable(
            "BLOKEBOT_DASHBOARD_VALUE_BENCHMARK_OUTPUT"
        );
        if (!string.IsNullOrWhiteSpace(destination))
        {
            await File.WriteAllTextAsync(destination, report);
        }
    }
}
