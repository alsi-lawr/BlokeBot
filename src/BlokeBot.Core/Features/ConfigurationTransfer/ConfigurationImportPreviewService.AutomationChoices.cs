using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationImportPreviewService
{
    public async Task<IReadOnlyList<AutomationExportChoice>> ListAutomationsAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .AutomationFlows.AsNoTracking()
            .Where(flow => flow.HostId == hostId)
            .OrderBy(flow => flow.Name)
            .ThenBy(flow => flow.Id)
            .Select(flow => new AutomationExportChoice(
                flow.Id,
                flow.Name,
                db.AutomationScenarios.Where(scenario => scenario.FlowId == flow.Id)
                    .OrderBy(scenario => scenario.Slot)
                    .Select(scenario => new AutomationScenarioExportChoice(
                        scenario.Id,
                        scenario.Name
                    ))
                    .ToArray()
            ))
            .ToArrayAsync(cancellationToken);
    }
}
