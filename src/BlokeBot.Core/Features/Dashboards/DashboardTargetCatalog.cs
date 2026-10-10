using System.Globalization;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed record DashboardTargetOption(string Key, string Label, DashboardTarget Target);

internal sealed class DashboardTargetCatalog(
    IDbContextFactory<BlokeBotDbContext> factory,
    DashboardAuthority authority
)
{
    internal async Task<DashboardResult<IReadOnlyList<DashboardTargetOption>>> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetKind kind,
        CancellationToken ct
    )
    {
        if (
            await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected denied
        )
        {
            return new DashboardResult<IReadOnlyList<DashboardTargetOption>>.Rejected(
                denied.Reason
            );
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        IReadOnlyList<DashboardTargetOption> options = kind switch
        {
            DashboardWidgetKind.Automation => (
                await db
                    .AutomationFlowNodes.AsNoTracking()
                    .Where(n =>
                        n.Flow.HostId == host.Value
                        && n.Flow.IsEnabled
                        && n.DefinitionId == AutomationDefinitionIds.ManualSource.Value
                    )
                    .Select(n => new
                    {
                        n.Id,
                        n.FlowId,
                        n.Flow.Name,
                        n.DisplayAlias,
                    })
                    .ToArrayAsync(ct)
            ).Select(n => new DashboardTargetOption($"{n.FlowId}/{n.Id}", $"{n.Name} · {n.DisplayAlias ?? "Manual Run"}", new DashboardTarget.Automation(new(n.FlowId), new(n.Id)))).ToArray(),
            DashboardWidgetKind.Value => (
                await db
                    .CustomValueDefinitions.AsNoTracking()
                    .Where(v => v.HostId == host.Value)
                    .OrderBy(v => v.Name)
                    .Select(v => new { v.Id, v.Name })
                    .ToArrayAsync(ct)
            )
                .Select(v => new DashboardTargetOption(
                    v.Id.ToString(CultureInfo.InvariantCulture),
                    v.Name,
                    new DashboardTarget.Value(v.Id)
                ))
                .ToArray(),
            DashboardWidgetKind.Countdown => [],
            DashboardWidgetKind.Poll => (
                await db
                    .TwitchPollTemplates.AsNoTracking()
                    .Where(t => t.HostId == host.Value)
                    .Select(t => new { t.Id, t.Title })
                    .ToArrayAsync(ct)
            )
                .Select(t => new DashboardTargetOption(
                    t.Id.ToString(CultureInfo.InvariantCulture),
                    t.Title,
                    new DashboardTarget.Poll(t.Id)
                ))
                .ToArray(),
            DashboardWidgetKind.Prediction => (
                await db
                    .TwitchPredictionTemplates.AsNoTracking()
                    .Where(t => t.HostId == host.Value)
                    .Select(t => new { t.Id, t.Title })
                    .ToArrayAsync(ct)
            )
                .Select(t => new DashboardTargetOption(
                    t.Id.ToString(CultureInfo.InvariantCulture),
                    t.Title,
                    new DashboardTarget.Prediction(t.Id)
                ))
                .ToArray(),
            DashboardWidgetKind.Giveaway =>
            [
                new("giveaway", "Channel giveaway", new DashboardTarget.Giveaway()),
            ],
            DashboardWidgetKind.Guessing => (
                await db
                    .Profiles.AsNoTracking()
                    .Where(p => p.HostId == host.Value)
                    .Select(p => new { p.Id, p.Name })
                    .ToArrayAsync(ct)
            )
                .Select(p => new DashboardTargetOption(
                    p.Id.ToString(CultureInfo.InvariantCulture),
                    p.Name,
                    new DashboardTarget.Guessing(p.Id)
                ))
                .ToArray(),
        };
        return new DashboardResult<IReadOnlyList<DashboardTargetOption>>.Succeeded(options);
    }
}
