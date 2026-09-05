using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Npgsql;

namespace BlokeBot.Simulation.Workloads;

internal static class PortalWorkload
{
    internal static async Task RunAsync(string[] arguments)
    {
        var provider = Required(arguments, "portal-workload");
        var store = Required(arguments, "workload-store");
        var output = Required(arguments, "workload-output");
        var viewers = Count(arguments, "workload-viewers", PortalWorkloadFixture.Viewers, 10001);
        var destinations = Count(arguments, "workload-destinations", 1, 6);
        var configuration = await ConfigurationAsync(provider, store);
        using var probe = new PortalWorkloadProbe();
        await using var simulation = await SimulationApplication.BuildAsync(
            arguments,
            CancellationToken.None,
            configuration,
            services =>
            {
                _ = services.AddSingleton(probe);
                _ = services.AddScoped<CircuitHandler, PortalWorkloadCircuitProbe>();
            }
        );
        await simulation.App.InitializeSimulationAsync(CancellationToken.None);
        await PortalWorkloadFixture.SeedAsync(simulation.App.Services, viewers, destinations);
        _ = simulation.App.Use(
            async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/channel"))
                {
                    using var sample = probe.Begin(
                        context.User.Identity?.IsAuthenticated == true
                            ? "http.authenticated"
                            : "http.anonymous"
                    );
                    await next(context);
                }
                else
                {
                    await next(context);
                }
            }
        );
        object Results() =>
            new
            {
                Protocol = "blokebot-portal-workload-v1",
                Provider = provider,
                AddedViewers = viewers,
                QueueAndBoardDestinations = destinations,
                AddedBacklog = PortalWorkloadFixture.Backlog,
                ReaderReadOperationsIncludeEndOfResultChecks = true,
                Samples = probe.Snapshot(),
            };
        _ = simulation.App.MapGet("/simulation/workload-results", () => Results());
        await simulation.App.StartAsync();
        await simulation
            .App.Services.GetRequiredService<SimulationStartupCoordinator>()
            .BootstrapAsync(simulation.App, CancellationToken.None);
        await using var scope = simulation.App.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var access = services.GetRequiredService<ViewerPortalAccess>();
        var resolved = await access.ResolveChannelAsync("samplechannel", CancellationToken.None);
        var channel = resolved.Match(
            value => value.Channel,
            _ => throw new InvalidOperationException("Synthetic channel missing.")
        );
        var catalogue = services.GetRequiredService<ViewerPortalCatalogueService>();
        var personal = services.GetRequiredService<PortalPersonalReader>();
        var session = new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = "1000",
            Login = "samplechannel",
            DisplayName = "Sample Channel",
        };
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var phase = iteration == 0 ? "warmup" : $"repeat-{iteration}";
            foreach (
                var descriptor in ViewerPortalCatalogue.Descriptors.Where(value =>
                    value.Audience == PortalAudience.Public
                )
            )
            {
                using var sample = probe.Begin($"{phase}.public.{descriptor.Feature}");
                var result = await catalogue.ReadAsync(
                    channel,
                    new PortalIdentity.Anonymous(),
                    CancellationToken.None,
                    new HashSet<BlokeBot.Persistence.Models.HostFeatureFlags> { descriptor.Feature }
                );
                sample.Outcome = string.Join(
                    ",",
                    result.Features.Select(feature =>
                        feature.Outcome.Match(
                            _ => "available",
                            _ => "empty",
                            _ => "disabled",
                            _ => "degraded",
                            _ => "unavailable",
                            _ => "unauthorized"
                        )
                    )
                );
                sample.SummaryJsonBytes = result.Features.Sum(feature =>
                    feature.Outcome.Match(
                        value => JsonSerializer.SerializeToUtf8Bytes(value.Summary).Length,
                        value => JsonSerializer.SerializeToUtf8Bytes(value.Summary).Length,
                        _ => 0,
                        value => JsonSerializer.SerializeToUtf8Bytes(value.Summary).Length,
                        _ => 0,
                        _ => 0
                    )
                );
            }
            foreach (var owner in Enum.GetValues<PortalSelfOwner>())
            {
                using var sample = probe.Begin($"{phase}.self.{owner}");
                var result = await personal.ReadAsync(
                    channel,
                    session,
                    owner,
                    CancellationToken.None
                );
                sample.Outcome = result.State.ToString();
                sample.SummaryJsonBytes = JsonSerializer.SerializeToUtf8Bytes(result.Items).Length;
            }
        }
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(Results(), new JsonSerializerOptions { WriteIndented = true })
        );
        if (arguments.Contains("--workload-serve=true", StringComparer.Ordinal))
        {
            await simulation.App.WaitForShutdownAsync();
            await File.WriteAllTextAsync(
                output,
                JsonSerializer.Serialize(
                    Results(),
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
    }

    private static int Count(string[] arguments, string name, int fallback, int maximum)
    {
        var value = arguments.SingleOrDefault(value =>
            value.StartsWith($"--{name}=", StringComparison.Ordinal)
        );
        var count = value is null
            ? fallback
            : int.Parse(
                value[(name.Length + 3)..],
                System.Globalization.CultureInfo.InvariantCulture
            );
        return count >= 1 && count <= maximum ? count : throw new ArgumentOutOfRangeException(name);
    }

    private static string Required(string[] arguments, string name) =>
        arguments
            .SingleOrDefault(value => value.StartsWith($"--{name}=", StringComparison.Ordinal))
            ?[(name.Length + 3)..]
        ?? throw new ArgumentException($"Missing --{name}= argument.");

    private static async Task<BlokeBotDatabaseConfiguration> ConfigurationAsync(
        string provider,
        string store
    )
    {
        if (provider == "Sqlite")
        {
            return File.Exists(store)
                ? throw new InvalidOperationException(
                    "The workload requires a new disposable SQLite path."
                )
                : BlokeBotDatabaseConfiguration.Sqlite(store);
        }
        if (provider != "PostgreSql")
        {
            throw new ArgumentException("Use Sqlite or PostgreSql.");
        }
        var configuration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(store);
        await using var connection = new NpgsqlConnection(
            (await File.ReadAllTextAsync(store)).Trim()
        );
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'",
            connection
        );
        return
            Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture
            ) == 0
            ? configuration
            : throw new InvalidOperationException(
                "The workload requires an empty disposable PostgreSQL database."
            );
    }
}
