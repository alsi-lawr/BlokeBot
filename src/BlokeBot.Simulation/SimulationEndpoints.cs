using BlokeBot.Commands;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Simulation;

internal static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this WebApplication app)
    {
        _ = app.MapGet(
                "/simulation/ready",
                static (SimulationReadiness readiness) =>
                {
                    var projection = readiness.Project();
                    return Results.Json(
                        projection,
                        statusCode: projection.Ready
                            ? StatusCodes.Status200OK
                            : StatusCodes.Status503ServiceUnavailable
                    );
                }
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/simulation/started",
                static (SimulationReadiness readiness) =>
                {
                    var projection = readiness.Project();
                    return Results.Json(
                        projection,
                        statusCode: projection.Started
                            ? StatusCodes.Status200OK
                            : StatusCodes.Status503ServiceUnavailable
                    );
                }
            )
            .AllowAnonymous();

        _ = app.MapGet(
                "/simulation/login",
                static (string? view, string? theme) =>
                {
                    var selectedTheme = string.Equals(
                        theme,
                        "dark",
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? "dark"
                        : "light";
                    var path = SimulationViewCatalog.PathFor(view);
                    var fragmentIndex = path.IndexOf('#');
                    var returnUrl =
                        fragmentIndex < 0
                            ? $"{path}?simulationTheme={selectedTheme}"
                            : $"{path[..fragmentIndex]}?simulationTheme={selectedTheme}{path[fragmentIndex..]}";
                    return Results.Redirect(
                        $"/auth/login?start=true&returnUrl={Uri.EscapeDataString(returnUrl)}"
                    );
                }
            )
            .AllowAnonymous();

        _ = app.MapPost(
                "/simulation/collectives/{state}",
                static async (
                    string state,
                    IDbContextFactory<BlokeBotDbContext> dbFactory,
                    HostFeatureService features,
                    CancellationToken ct
                ) =>
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    var hostId = await db
                        .Hosts.Where(value => value.Login == SimulationMode.Login)
                        .Select(value => value.Id)
                        .SingleAsync(ct);
                    if (string.Equals(state, "enabled", StringComparison.Ordinal))
                    {
                        await features.EnableAsync(hostId, HostFeatureFlags.Collectives, ct);
                        return Results.Ok();
                    }
                    if (string.Equals(state, "disabled", StringComparison.Ordinal))
                    {
                        await features.DisableAsync(hostId, HostFeatureFlags.Collectives, ct);
                        return Results.Ok();
                    }
                    return Results.BadRequest();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/liveness/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetLivenessAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/round/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetRoundAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/giveaway/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetGiveawayAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/features/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetFeatureAvailabilityAsync(state, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/alerts/{state}",
                static async (
                    string state,
                    SimulationCommandCatalogScenario scenario,
                    DurableAlertService alerts,
                    CancellationToken ct
                ) =>
                {
                    await scenario.SetAlertsAsync(state, alerts, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/simulation/commands/catalog",
                static async (
                    SimulationCommandCatalogScenario scenario,
                    ViewerCommandCatalogService catalog,
                    CancellationToken ct
                ) => Results.Json(await scenario.SnapshotAsync(catalog, ct))
            )
            .AllowAnonymous();
        _ = app.MapPost(
                "/simulation/commands/chat",
                static async (
                    SimulationCommandCatalogScenario scenario,
                    ChatCommandDispatcher dispatcher,
                    CancellationToken ct
                ) => Results.Json(new { messages = await scenario.DispatchAsync(dispatcher, ct) })
            )
            .AllowAnonymous();
    }
}
