using BlokeBot.Commands;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Simulation.FakeTwitch;

namespace BlokeBot.Simulation;

internal static class SimulationDashboardRecentViewersFixture
{
    internal static async Task SeedAsync(DashboardRecentViewers viewers, CancellationToken ct)
    {
        for (var sample = 1; sample <= 30; sample++)
        {
            await ObserveAsync(viewers, sample, ct);
        }
    }

    internal static void Map(WebApplication app) =>
        _ = app.MapPost(
                "/simulation/dashboard/chat/{sample:int}",
                static async (int sample, DashboardRecentViewers viewers, CancellationToken ct) =>
                {
                    if (sample is < 1 or > 31)
                    {
                        return Results.BadRequest();
                    }
                    await ObserveAsync(viewers, sample, ct);
                    return Results.Ok();
                }
            )
            .AllowAnonymous();

    private static ValueTask ObserveAsync(
        DashboardRecentViewers viewers,
        int sample,
        CancellationToken ct
    ) =>
        viewers.MessageReceivedAsync(
            new ChatMessage(
                $"sampleviewer{sample:00}",
                FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login,
                "simulation chatter observation",
                "simulation",
                new Dictionary<string, string>
                {
                    ["user-id"] = $"3700000{sample:00}",
                    ["display-name"] = $"Sample viewer {sample:00}",
                }
            ),
            ct
        );
}
