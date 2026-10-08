using System.Security.Claims;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class VariablesPageTests
{
    [Test]
    public async Task StaleManualEditAndChangedChannelRetainInputWithoutOverwritingValues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int otherId;
        await using (var seed = database.CreateDbContext())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = DateTime.UtcNow,
            };
            var other = new BotHost
            {
                Login = "other",
                DisplayName = "Other",
                CreatedAtUtc = DateTime.UtcNow,
            };
            seed.Hosts.AddRange(host, other);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            otherId = other.Id;
        }
        var values = new CustomStoredValueService(database);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        var ui = UiTestContextFactory.CreateWithAuthorization(database, hostId);
        await using var context = ui.Context;
        _ = context.Services.AddSingleton(
            new ModeratorAuthorityService(null!, null!, null!, null!, TimeProvider.System)
        );
        var page = context.Render<VariablesPage>();
        page.WaitForAssertion(() => page.Find("button[aria-current='false']").Click());
        page.Find("#saved-value").Input("99");
        var original = (
            await values.ValueAsync(hostId, new(definition.Id, string.Empty, string.Empty), default)
        )!;
        (
            await values.SaveValueAsync(hostId, original, CustomValueKind.Number, "7", default)
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Save changes").Click();
        page.FindComponents<TextAreaField>()
            .Single(x => x.Instance.Id == "saved-value")
            .Instance.Value.ShouldBe("99");
        (await values.ValueAsync(hostId, original.Target, default))!.Value.ShouldBe("7");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Reload current value").Click();
        page.FindComponents<TextAreaField>()
            .Single(x => x.Instance.Id == "saved-value")
            .Instance.Value.ShouldBe("7");
        page.Find("#variables-dictionaries-tab").Click();
        page.Find("#variables-dictionaries-tab").GetAttribute("aria-selected").ShouldBe("true");
        page.Find("#variables-dictionaries-panel")
            .GetAttribute("aria-labelledby")
            .ShouldBe("variables-dictionaries-tab");
        page.Find("#variables-variables-tab").Click();
        page.Find("#variables-variables-tab").GetAttribute("aria-selected").ShouldBe("true");
        page.Find("button[aria-current='false']").Click();
        page.Find("#saved-value").Input("123");
        var otherChoice = new BotHostChoice(otherId, "other", "Other", AuthRole.Streamer);
        _ = ui.Authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "other-id"),
            new Claim(ClaimTypes.Name, "other"),
            new Claim(AuthClaims.Login, "other"),
            new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Streamer)),
            new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(otherChoice)),
            new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(otherChoice))
        );
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Save changes").Click();
        page.FindComponents<TextAreaField>()
            .Single(x => x.Instance.Id == "saved-value")
            .Instance.Value.ShouldBe("123");
        (await values.ValueAsync(hostId, original.Target, default))!.Value.ShouldBe("7");
        (await values.DefinitionsAsync(otherId, default)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReplyEditorSandboxReevaluatesCopiesWithoutLiveEffectsOrInvocationState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = database.CreateDbContext())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
        }
        var values = new CustomStoredValueService(database);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "4"
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        var preview = context.Render<StoredTokenPreview>(parameters =>
            parameters
                .Add(x => x.Id, "sample")
                .Add(x => x.HostId, hostId)
                .Add(x => x.Channel, "streamer")
                .Add(x => x.Text, "{var_inc|global|total|1}:{var_get|global|total}")
        );
        preview.Find("output").TextContent.ShouldBe("5:5");
        preview.Find("#sample-args").Input("another sample");
        preview.Find("output").TextContent.ShouldBe("5:5");
        preview.Render();
        preview.Find("output").TextContent.ShouldBe("5:5");
        (
            await values.ValueAsync(hostId, new(definition.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("4");
        await using var verify = database.CreateDbContext();
        (await verify.CustomStoredValues.ToListAsync()).ShouldBeEmpty();
        (await verify.CustomCommandInvocationClaims.ToListAsync()).ShouldBeEmpty();
        (await verify.CustomCommandComputedResults.ToListAsync()).ShouldBeEmpty();
    }
}
