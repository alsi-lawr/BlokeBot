namespace BlokeBot.Simulation;

internal sealed record SimulationDashboardProviderCounts(
    int PollControlReads,
    int PredictionControlReads,
    int PollVoteMutations
);

internal static class SimulationDashboardAuditFixture
{
    internal static void Map(WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Simulation:DashboardAudit"))
        {
            return;
        }
        app.Services.GetRequiredService<SimulationNativeTwitchDashboardOperations>().EnableAudit();
        _ = app.MapGet(
                "/simulation/dashboard/audit/provider-counts",
                static (SimulationNativeTwitchDashboardOperations operations) =>
                    Results.Json(operations.AuditCounts())
            )
            .AllowAnonymous();
    }
}
