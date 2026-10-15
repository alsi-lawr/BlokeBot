using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomCommandExecutionTests
{
    [Test]
    public async Task CommandArguments_LiveOriginalTailRejectsBeforeClaimAndCooldownThenSharesTypedValues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "streamer");
        var command = await SeedAutomationCommandAsync(
            database,
            host,
            "timer",
            true,
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var chat = new RecordingPublicChatSender();
        await using var services = BuildServices(database, publicChat: chat, realAutomations: true);
        var configuration = ActivatorUtilities.CreateInstance<CustomCommandConfigurationService>(
            services,
            new ArgumentAnnouncementReadiness()
        );
        var draft = await configuration.LoadConfigurationAsync(host, default);
        draft.Commands.Single().ArgumentSchema = CustomCommandArgumentTests.TimerSchema;
        var save = CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                value => value,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );
        _ = (await configuration.SaveConfiguration(host, save).ExecuteAsync(default)).Match(
            value => value,
            failure => throw new InvalidOperationException(failure.Message)
        );
        var flows = services.GetRequiredService<AutomationFlowService>();
        for (var index = 0; index < 2; index++)
        {
            var source = AutomationNode(
                "custom-command",
                $$"""{"custom-command-id":{{command.CommandId}}}"""
            );
            var action = AutomationNode("send-chat", """{"message":""}""") with
            {
                InputBindings = ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationInputBinding
                >.Empty.Add(new("message"), new(AutomationInputBindingMode.Connected, null)),
            };
            _ = (
                await flows.SaveAsync(
                    new(
                        null,
                        new(host),
                        $"Typed timer {index}",
                        AutomationFlowSchema.CurrentVersion,
                        true,
                        [source, action],
                        [
                            new(
                                Guid.NewGuid(),
                                AutomationEdgeKind.Flow,
                                source.Id,
                                new("flow"),
                                action.Id,
                                new("flow")
                            ),
                            new(
                                Guid.NewGuid(),
                                AutomationEdgeKind.Data,
                                source.Id,
                                new("argument.name"),
                                action.Id,
                                new("message")
                            ),
                        ]
                    ),
                    default
                )
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        }
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> feedback = [];
        foreach (
            var tail in new[]
            {
                "secret-invalid tea",
                "0 tea",
                "1 " + new string('x', 499),
                new string(' ', 496) + "1 tea",
            }
        )
        {
            await DispatchMessageAsync(
                dispatcher,
                "viewer",
                "streamer",
                "!timer " + tail,
                feedback
            );
        }
        feedback.Count.ShouldBe(4);
        feedback.ShouldAllBe(message =>
            message.Contains("Usage: !timer", StringComparison.Ordinal)
        );
        string.Join(' ', feedback).ShouldNotContain("secret-invalid");
        services.GetRequiredService<CustomCommandCooldownStore>().EntryCount.ShouldBe(0);
        chat.Messages.ShouldBeEmpty();
        await using (var before = await database.CreateDbContextAsync())
        {
            (await before.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
            (await before.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        // Exactly 500 original-tail characters, including the second separating whitespace.
        var valid = "\t1 " + new string('n', 497);
        valid.Length.ShouldBe(500);
        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!timer " + valid, feedback);
        services.GetRequiredService<CustomCommandCooldownStore>().EntryCount.ShouldBe(1);
        chat.Messages.ShouldBe([new string('n', 497), new string('n', 497)]);
        await using var db = await database.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
        var runs = await db.AutomationFlowRuns.ToArrayAsync();
        runs.Length.ShouldBe(2);
        foreach (var run in runs)
        {
            var context = AutomationRuntimeSerialization
                .RestoreContext(run.ContextSchemaVersion, run.ContextJson)
                .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
                .Context;
            context
                .CommandArguments.ShouldNotBeNull()
                .Values["seconds"]
                .ShouldBe(new AutomationValue.Number(1));
            context
                .CommandArguments.Values["name"]
                .ShouldBe(new AutomationValue.Text(new string('n', 497)));
            context.Arguments[0].Value.ShouldBe("1");
        }
        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!timer 1 repeated", feedback);
        chat.Messages.Count.ShouldBe(2);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task CommandArguments_ConcurrentSchemaSaveWinsLockedAdmissionWithoutConsumingClaimOrCooldown()
    {
        var cut = new AutomationDispatchTransactionInterleaving();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(cut);
        var host = await SeedHostAsync(database, "streamer");
        var command = await SeedAutomationCommandAsync(
            database,
            host,
            "timer",
            true,
            cooldownSeconds: 30,
            invocationLimit: CustomCommandInvocationLimit.OncePerUser
        );
        var chat = new RecordingPublicChatSender();
        await using var services = BuildServices(database, publicChat: chat, realAutomations: true);
        var configuration = ActivatorUtilities.CreateInstance<CustomCommandConfigurationService>(
            services,
            new ArgumentAnnouncementReadiness()
        );
        var draft = await configuration.LoadConfigurationAsync(host, default);
        draft.Commands.Single().ArgumentSchema = CustomCommandArgumentTests.TimerSchema;
        await SaveArgumentsConfiguration(configuration, host, draft);
        await SeedAutomationFlowAsync(services, host, command.CommandId);
        await services.GetRequiredService<AutomationRuntimeService>().InitializeAsync(default);
        var dispatcher = services.GetRequiredService<ChatCommandDispatcher>();
        List<string> replies = [];
        cut.Arm();
        var invocation = DispatchMessageAsync(
            dispatcher,
            "viewer",
            "streamer",
            "!timer 1 tea",
            replies
        );
        await cut.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            draft.Commands.Single().ArgumentSchema = CustomCommandArgumentTests.TimerSchema with
            {
                Fields = CustomCommandArgumentTests.TimerSchema.Fields.SetItem(
                    0,
                    CustomCommandArgumentTests.TimerSchema.Fields[0] with
                    {
                        ExclusiveMinimum = 2m,
                    }
                ),
            };
            await SaveArgumentsConfiguration(configuration, host, draft);
        }
        finally
        {
            cut.Release();
        }
        await invocation.WaitAsync(TimeSpan.FromSeconds(5));
        chat.Messages.ShouldBeEmpty();
        services.GetRequiredService<CustomCommandCooldownStore>().EntryCount.ShouldBe(0);
        await using (var before = await database.CreateDbContextAsync())
        {
            (await before.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
            (await before.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        await DispatchMessageAsync(dispatcher, "viewer", "streamer", "!timer 3 tea", replies);
        chat.Messages.ShouldBe(["automation ran"]);
        services.GetRequiredService<CustomCommandCooldownStore>().EntryCount.ShouldBe(1);
        await using var db = await database.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    private static async Task SaveArgumentsConfiguration(
        CustomCommandConfigurationService service,
        int host,
        CustomCommandConfiguration draft
    )
    {
        var validated = CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                value => value,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );
        _ = (await service.SaveConfiguration(host, validated).ExecuteAsync(default)).Match(
            value => value,
            failure => throw new InvalidOperationException(failure.Message)
        );
    }

    private sealed class ArgumentAnnouncementReadiness : ITwitchAnnouncementReadinessProvider
    {
        public Task<TwitchAnnouncementReadiness> GetReadinessAsync(
            string channel,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new TwitchAnnouncementReadiness(
                    TwitchAnnouncementAvailability.Unavailable,
                    string.Empty
                )
            );
    }
}
