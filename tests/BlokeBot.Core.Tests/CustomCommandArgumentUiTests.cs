using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandArgumentUiTests
{
    [Test]
    public async Task CommandArguments_StudioEditsBoundsAndRequiredRestThroughSaveReloadAndRevealsInvalidField()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int commandId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            var command = new CustomCommand
            {
                HostId = host.Id,
                Name = "Timer",
                Enabled = true,
                AllowEveryone = true,
                Action = new AutomationCustomCommandAction { HostId = host.Id },
                Aliases = [new() { HostId = host.Id, Alias = "timer" }],
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _ = db.CustomCommands.Add(command);
            _ = await db.SaveChangesAsync();
            commandId = command.Id;
        }
        await using var context = UiTestContextFactory.Create(database, hostId);
        var page = context.Render<CustomCommandSettingsPage>();
        page.Find("button[aria-controls='studio-stage-arguments']").Click();
        page.Find("#studio-stage-arguments button[role='switch']").Click();
        var stage = page.FindComponent<CustomCommandArgumentsStage>();
        stage.Instance.Command.ArgumentSchema!.Fields[0].ExclusiveMinimum.ShouldBe(0);
        stage.FindAll("details input")[0].Change("0.0000000000000000000000000001");
        stage.FindAll("details input")[2].Change("10");
        page.Find("button[aria-label='Save custom commands']").Click();
        page.WaitForAssertion(() =>
            page.Find("button[aria-label='Save custom commands']")
                .HasAttribute("disabled")
                .ShouldBeTrue()
        );
        page.Dispose();
        page = context.Render<CustomCommandSettingsPage>();
        page.Find("button[aria-controls='studio-stage-arguments']").Click();
        var reopened = page.FindComponent<CustomCommandArgumentsStage>();
        page.Find($"#command-{commandId}-argument-0")
            .GetAttribute("value")
            .ShouldBe(reopened.Instance.Command.ArgumentSchema!.Fields[0].Name);
        page.Find($"#command-{commandId}-argument-1")
            .GetAttribute("value")
            .ShouldBe(reopened.Instance.Command.ArgumentSchema!.Fields[1].Name);
        reopened
            .Instance.Command.ArgumentSchema!.Fields[0]
            .ExclusiveMinimum.ShouldBe(0.0000000000000000000000000001m);
        reopened.Instance.Command.ArgumentSchema.Fields[1].MaximumWords.ShouldBe(10);
        page.Find($"#command-{commandId}-argument-0").Input("INVALID NAME");
        page.Find("button[aria-controls='studio-stage-arguments']").Click();
        page.Find("button[aria-label='Save custom commands']").Click();
        page.Find("button[aria-controls='studio-stage-arguments']")
            .GetAttribute("aria-expanded")
            .ShouldBe("true");
        page.Find($"#command-{commandId}-argument-0").GetAttribute("aria-invalid").ShouldBe("true");
        await using var verify = await database.CreateDbContextAsync();
        var schema = (await verify.CustomCommands.SingleAsync()).ArgumentSchema.ShouldNotBeNull();
        schema.Fields[0].Name.ShouldBe("seconds");
        schema.Fields[0].ExclusiveMinimum.ShouldBe(0.0000000000000000000000000001m);
        schema.Fields[1].MaximumWords.ShouldBe(10);
    }
}
