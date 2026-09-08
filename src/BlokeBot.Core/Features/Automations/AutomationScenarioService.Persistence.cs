using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService
{
    public async Task<ImmutableArray<AutomationScenarioSnapshot>> ListAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var scenarios = await Owned(db, hostId, flowId)
            .AsNoTracking()
            .OrderBy(value => value.Slot)
            .ToArrayAsync(cancellationToken);
        return
        [
            .. scenarios.Select(value => new AutomationScenarioSnapshot(
                new(value.Id),
                flowId,
                value.Name,
                AutomationScenarioSerialization.Deserialize(value.FixtureJson)
            )),
        ];
    }

    public async Task<AutomationScenarioAuthoringOutcome> SaveAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        AutomationScenarioId? scenarioId,
        string name,
        AutomationScenarioFixture fixture,
        CancellationToken cancellationToken
    )
    {
        if (!ValidName(name))
        {
            return InvalidName();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (
            scenarioId is { } requestedId
            && !await Owned(db, hostId, flowId)
                .AnyAsync(value => value.Id == requestedId.Value, cancellationToken)
        )
        {
            return new(AutomationScenarioAuthoringStatus.NotFound);
        }
        var flow = await LoadFlowAsync(db, hostId, flowId, cancellationToken);
        if (flow is null)
        {
            return new(AutomationScenarioAuthoringStatus.NotFound);
        }
        if (
            AutomationFlowService.RestoreDraft(flow)
            is not AutomationFlowDraftRestoreOutcome.Available restored
        )
        {
            return new(AutomationScenarioAuthoringStatus.Invalid);
        }
        var validation = await ValidateAsync(restored.Draft, fixture, cancellationToken);
        if (validation.Gate is not null)
        {
            return new(
                validation.Gate == AutomationCatalogAvailability.Disabled
                    ? AutomationScenarioAuthoringStatus.FeatureDisabled
                    : AutomationScenarioAuthoringStatus.HostNotFound
            );
        }
        if (!validation.Errors.IsEmpty)
        {
            return new(AutomationScenarioAuthoringStatus.Invalid, Errors: validation.Errors);
        }
        AutomationScenario scenario;
        if (scenarioId is { } existingId)
        {
            var existing = await Owned(db, hostId, flowId)
                .SingleOrDefaultAsync(value => value.Id == existingId.Value, cancellationToken);
            if (existing is null)
            {
                return new(AutomationScenarioAuthoringStatus.NotFound);
            }
            scenario = existing;
        }
        else
        {
            var slots = await Owned(db, hostId, flowId)
                .Select(value => value.Slot)
                .ToArrayAsync(cancellationToken);
            var free = Enumerable.Range(0, MaximumScenariosPerFlow).Except(slots).ToArray();
            if (free.Length == 0)
            {
                return new(AutomationScenarioAuthoringStatus.LimitReached);
            }
            scenario = new()
            {
                Id = Guid.NewGuid(),
                FlowId = flowId.Value,
                Slot = free[0],
            };
            _ = db.AutomationScenarios.Add(scenario);
        }
        scenario.Name = name.Trim();
        scenario.FixtureJson = AutomationScenarioSerialization.Serialize(fixture);
        try
        {
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(AutomationScenarioAuthoringStatus.NotFound);
        }
        catch (DbUpdateException) when (scenarioId is null)
        {
            await using var fresh = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (
                await Owned(fresh, hostId, flowId)
                    .AnyAsync(value => value.Slot == scenario.Slot, cancellationToken)
            )
            {
                return new(AutomationScenarioAuthoringStatus.Conflict);
            }
            throw;
        }
        return new(AutomationScenarioAuthoringStatus.Saved, new(scenario.Id));
    }

    public async Task<AutomationScenarioAuthoringOutcome> RenameAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        AutomationScenarioId scenarioId,
        string name,
        CancellationToken cancellationToken
    )
    {
        if (!ValidName(name))
        {
            return InvalidName();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var updated = await Owned(db, hostId, flowId)
            .Where(value => value.Id == scenarioId.Value)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.Name, name.Trim()),
                cancellationToken
            );
        return new(
            updated == 0
                ? AutomationScenarioAuthoringStatus.NotFound
                : AutomationScenarioAuthoringStatus.Saved,
            scenarioId
        );
    }

    public async Task<AutomationScenarioAuthoringOutcome> DuplicateAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        AutomationScenarioId scenarioId,
        string name,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var scenario = await Owned(db, hostId, flowId)
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == scenarioId.Value, cancellationToken);
        return scenario is null
            ? new(AutomationScenarioAuthoringStatus.NotFound)
            : await SaveAsync(
                hostId,
                flowId,
                null,
                name,
                AutomationScenarioSerialization.Deserialize(scenario.FixtureJson),
                cancellationToken
            );
    }

    public async Task<AutomationScenarioAuthoringOutcome> DeleteAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        AutomationScenarioId scenarioId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await Owned(db, hostId, flowId)
            .Where(value => value.Id == scenarioId.Value)
            .ExecuteDeleteAsync(cancellationToken);
        return new(
            deleted == 0
                ? AutomationScenarioAuthoringStatus.NotFound
                : AutomationScenarioAuthoringStatus.Deleted
        );
    }

    public async Task<AutomationScenarioRunOutcome> RunSavedAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        AutomationScenarioId scenarioId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var scenario = await Owned(db, hostId, flowId)
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == scenarioId.Value, cancellationToken);
        var flow = await LoadFlowAsync(db, hostId, flowId, cancellationToken);
        return
            scenario is not null
            && flow is not null
            && AutomationFlowService.RestoreDraft(flow)
                is AutomationFlowDraftRestoreOutcome.Available restored
            ? await RunAsync(
                restored.Draft,
                AutomationScenarioSerialization.Deserialize(scenario.FixtureJson),
                cancellationToken
            )
            : new AutomationScenarioRunOutcome.Invalid([
                new(
                    null,
                    "scenario-not-found",
                    "Select a saved scenario belonging to this flow and channel."
                ),
            ]);
    }

    private static IQueryable<AutomationScenario> Owned(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        AutomationFlowId flowId
    ) =>
        db.AutomationScenarios.Where(value =>
            value.FlowId == flowId.Value && value.Flow.HostId == hostId.Value
        );

    private static Task<AutomationFlow?> LoadFlowAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        AutomationFlowId flowId,
        CancellationToken cancellationToken
    ) =>
        db
            .AutomationFlows.AsNoTracking()
            .Include(value => value.Nodes)
            .Include(value => value.Edges)
            .SingleOrDefaultAsync(
                value => value.Id == flowId.Value && value.HostId == hostId.Value,
                cancellationToken
            );

    private static bool ValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200;

    private static AutomationScenarioAuthoringOutcome InvalidName() =>
        new(
            AutomationScenarioAuthoringStatus.Invalid,
            Errors:
            [
                new(null, "scenario-name-invalid", "Enter a scenario name of 1 to 200 characters."),
            ]
        );
}
