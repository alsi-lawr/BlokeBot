using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Simulation;

internal static class SimulationDashboardFixtures
{
    internal static readonly Guid ControlsId = Guid.Parse("d3390000-0000-4000-8000-000000000007");
    internal static readonly Guid BenchmarkId = Guid.Parse("d3390000-0000-4000-8000-000000000030");

    internal static async Task SeedAsync(
        BlokeBotDbContext db,
        int hostId,
        SimulationNativeTwitchDashboardOperations nativeControls,
        CancellationToken ct
    )
    {
        var now = SimulationMode.Now.UtcDateTime;
        var poll = await db
            .TwitchPollTemplates.Include(t => t.Choices)
            .FirstOrDefaultAsync(t => t.HostId == hostId, ct);
        if (poll is null)
        {
            poll = new()
            {
                HostId = hostId,
                Title = "What should we play next?",
                DurationSeconds = 60,
                CreatedAtUtc = now,
                Choices =
                [
                    new() { Title = "Puzzle game", Position = 0 },
                    new() { Title = "Adventure game", Position = 1 },
                ],
            };
            _ = db.TwitchPollTemplates.Add(poll);
        }
        var prediction = await db
            .TwitchPredictionTemplates.Include(t => t.Outcomes)
            .FirstOrDefaultAsync(t => t.HostId == hostId, ct);
        if (prediction is null)
        {
            prediction = new()
            {
                HostId = hostId,
                Title = "Will we finish this level?",
                PredictionWindowSeconds = 60,
                CreatedAtUtc = now,
                Outcomes =
                [
                    new() { Title = "Yes", Position = 0 },
                    new() { Title = "Not this time", Position = 1 },
                ],
            };
            _ = db.TwitchPredictionTemplates.Add(prediction);
        }
        _ = await db.SaveChangesAsync(ct);
        nativeControls.SetTemplates(
            hostId,
            (
                await db
                    .TwitchPollTemplates.Include(t => t.Choices)
                    .Where(t => t.HostId == hostId)
                    .ToArrayAsync(ct)
            )
                .Select(t => new PollTemplateView(
                    t.Id,
                    t.Title,
                    t.Choices.OrderBy(c => c.Position).Select(c => c.Title).ToArray(),
                    t.DurationSeconds,
                    t.ChannelPointsVotingEnabled,
                    t.ChannelPointsPerVote
                ))
                .ToArray(),
            (
                await db
                    .TwitchPredictionTemplates.Include(t => t.Outcomes)
                    .Where(t => t.HostId == hostId)
                    .ToArrayAsync(ct)
            )
                .Select(t => new PredictionTemplateView(
                    t.Id,
                    t.Title,
                    t.Outcomes.OrderBy(o => o.Position).Select(o => o.Title).ToArray(),
                    t.PredictionWindowSeconds
                ))
                .ToArray()
        );
        if (await db.ChannelDashboards.AnyAsync(d => d.HostId == hostId, ct))
        {
            return;
        }
        var manual = await db.AutomationFlowNodes.FirstOrDefaultAsync(
            n =>
                n.Flow.HostId == hostId
                && n.Flow.IsEnabled
                && n.DefinitionId == AutomationDefinitionIds.ManualSource.Value,
            ct
        );
        if (manual is null)
        {
            var flow = new AutomationFlow
            {
                Id = Guid.NewGuid(),
                HostId = hostId,
                Name = "Dashboard announcement",
                SchemaVersion = 1,
                IsEnabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            manual = new()
            {
                Id = Guid.NewGuid(),
                FlowId = flow.Id,
                DefinitionId = AutomationDefinitionIds.ManualSource.Value,
                DefinitionSchemaVersion = 1,
                ConfigurationJson = "{\"data\":\"Tea is ready\"}",
                InputBindingsJson = "{}",
                ExpressionLanguageVersion = 1,
            };
            flow.Nodes.Add(manual);
            _ = db.AutomationFlows.Add(flow);
            _ = await db.SaveChangesAsync(ct);
        }
        var value = await db.CustomValueDefinitions.FirstOrDefaultAsync(
            v => v.HostId == hostId,
            ct
        );
        if (value is null)
        {
            value = new()
            {
                HostId = hostId,
                Name = "Yawn counter",
                Scope = CustomValueScope.User,
                Kind = CustomValueKind.Number,
                DefaultNumber = 5,
                Revision = Guid.NewGuid(),
            };
            _ = db.CustomValueDefinitions.Add(value);
            _ = await db.SaveChangesAsync(ct);
        }
        var profile = await db
            .Profiles.Where(p => p.HostId == hostId)
            .Select(p => p.Id)
            .FirstAsync(ct);
        DashboardBinding Bound(DashboardTarget target) => new DashboardBinding.Configured(target);
        DashboardWidgetConfiguration[] configurations =
        [
            new DashboardWidgetConfiguration.Automation(
                Bound(new DashboardTarget.Automation(new(manual.FlowId), new(manual.Id))),
                new DashboardTextOverride.Inherit()
            ),
            new DashboardWidgetConfiguration.Value(
                Bound(new DashboardTarget.Value(value.Id)),
                new DashboardValueSelection.Scalar()
            ),
            new DashboardWidgetConfiguration.Countdown(
                Bound(new DashboardTarget.Countdown("Break")),
                TimeSpan.FromMinutes(5)
            ),
            new DashboardWidgetConfiguration.Poll(Bound(new DashboardTarget.Poll(poll.Id))),
            new DashboardWidgetConfiguration.Prediction(
                Bound(new DashboardTarget.Prediction(prediction.Id))
            ),
            new DashboardWidgetConfiguration.Giveaway(Bound(new DashboardTarget.Giveaway())),
            new DashboardWidgetConfiguration.Guessing(Bound(new DashboardTarget.Guessing(profile))),
        ];
        string[] labels =
        [
            "Stream announcement",
            "Yawn counter",
            "Break countdown",
            "Next game",
            "Build prediction",
            "Community giveaway",
            "Guess the time",
        ];
        Add(ControlsId, "Stream controls", 7);
        Add(BenchmarkId, "Large dashboard (30 widgets)", 30);
        var disabledFlow = new AutomationFlow
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            Name = "Paused dashboard action",
            SchemaVersion = 1,
            IsEnabled = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var disabledSource = new AutomationFlowNode
        {
            Id = Guid.NewGuid(),
            FlowId = disabledFlow.Id,
            Flow = disabledFlow,
            DefinitionId = AutomationDefinitionIds.ManualSource.Value,
            DefinitionSchemaVersion = 1,
            ConfigurationJson = "{\"data\":\"Paused\"}",
            InputBindingsJson = "{}",
            ExpressionLanguageVersion = 1,
        };
        _ = db.AutomationFlowNodes.Add(disabledSource);
        var unavailable = new DashboardDocument(
            12,
            [
                new(
                    new(Guid.NewGuid()),
                    "Missing poll template",
                    new(0, 0, 6, 5, 0),
                    new DashboardWidgetConfiguration.Poll(
                        Bound(new DashboardTarget.Poll(int.MaxValue))
                    )
                ),
                new(
                    new(Guid.NewGuid()),
                    "Choose a target",
                    new(6, 0, 6, 5, 1),
                    new DashboardWidgetConfiguration.Value(
                        new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected),
                        new DashboardValueSelection.Scalar()
                    )
                ),
            ]
        );
        unavailable = unavailable with
        {
            Widgets = unavailable.Widgets.Add(
                new(
                    new(Guid.NewGuid()),
                    "Disabled automation",
                    new(0, 5, 6, 5, 2),
                    new DashboardWidgetConfiguration.Automation(
                        Bound(
                            new DashboardTarget.Automation(
                                new(disabledFlow.Id),
                                new(disabledSource.Id)
                            )
                        ),
                        new DashboardTextOverride.Inherit()
                    )
                )
            ),
        };
        _ = db.ChannelDashboards.Add(
            new()
            {
                Id = Guid.NewGuid(),
                HostId = hostId,
                Name = "Targets needing attention",
                Revision = 1,
                DocumentJson = DashboardDocuments.Serialize(unavailable),
            }
        );
        _ = await db.SaveChangesAsync(ct);
        void Add(Guid id, string name, int count)
        {
            var widgets = Enumerable
                .Range(0, count)
                .Select(i => new DashboardWidget(
                    new(Guid.NewGuid()),
                    i < 7 ? labels[i] : $"{labels[i % 7]} {(i / 7) + 1}",
                    new(i % 2 * 6, i / 2 * 5, 6, 5, i),
                    configurations[i % 7]
                ))
                .ToImmutableArray();
            _ = db.ChannelDashboards.Add(
                new()
                {
                    Id = id,
                    HostId = hostId,
                    Name = name,
                    Revision = 1,
                    DocumentJson = DashboardDocuments.Serialize(new(12, widgets)),
                }
            );
        }
    }
}
