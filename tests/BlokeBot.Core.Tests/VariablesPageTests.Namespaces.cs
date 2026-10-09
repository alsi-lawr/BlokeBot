using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class VariablesPageTests
{
    [Test]
    public async Task SameNameUserDefinitions_SaveAndReopenTheIntendedManagementCategory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = database.CreateDbContext())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var ui = UiTestContextFactory.CreateWithAuthorization(database, hostId);
        await using var context = ui.Context;
        _ = context.Services.AddSingleton(
            new ModeratorAuthorityService(null!, null!, null!, null!, TimeProvider.System)
        );
        _ = context.Services.AddSingleton<ICustomCommandViewerResolver>(new NamespaceViewer());
        var page = context.Render<VariablesPage>();
        _ = page.WaitForElement("#variables-variables-tab");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "+ New variable").Click();
        page.Find("#variable-name").Input("profile");
        page.Find("#variable-default").Input("2");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Save changes").Click();
        page.Find("#variable-viewer").Input("viewer");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Select viewer").Click();
        page.Find("#saved-value").Input("5");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Save changes").Click();
        page.Find("#variables-dictionaries-tab").Click();
        page.FindAll("button").Single(x => x.TextContent.Trim() == "+ New dictionary").Click();
        page.Find("#variable-name").Input("profile");
        page.Find("#variable-default").Input("missing");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Save changes").Click();
        page.FindAll("button").Single(x => x.TextContent.Trim() == "+ Add entry").Click();
        page.Find("#dictionary-key").Input("game");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Open entry").Click();
        page.Find("#saved-value").Input("Celeste");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Save changes").Click();
        page.Find("#definition-default").Input("fallback");
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Save definition").Click();
        page.Find("#variables-variables-tab").Click();
        page.Find(".studio-rail__item").Click();
        page.FindComponents<BlokeBot.Core.Components.TextAreaField>()
            .Single(x => x.Instance.Id == "saved-value")
            .Instance.Value.ShouldBe("5");
        page.Find("#variables-dictionaries-tab").Click();
        page.Find(".studio-rail__item").Click();
        page.FindAll("button").Single(x => x.TextContent.Trim() == "Edit").Click();
        page.FindComponents<BlokeBot.Core.Components.TextAreaField>()
            .Single(x => x.Instance.Id == "saved-value")
            .Instance.Value.ShouldBe("Celeste");
        var values = new CustomStoredValueService(database);
        var definitions = await values.DefinitionsAsync(hostId, default);
        var scalar = definitions.Single(x => x.Kind == CustomValueKind.Number);
        var dictionary = definitions.Single(x => x.Kind == CustomValueKind.Dictionary);
        dictionary.Default.ShouldBe("fallback");
        scalar.Default.ShouldBe("2");
        (
            await values.ValueAsync(hostId, new(scalar.Id, "stable-viewer", string.Empty), default)
        )!.Value.ShouldBe("5");
        (
            await values.ValueAsync(hostId, new(dictionary.Id, "stable-viewer", "game"), default)
        )!.Value.ShouldBe("Celeste");
    }

    private sealed class NamespaceViewer : ICustomCommandViewerResolver
    {
        public Task<CustomCommandViewerResolution> ResolveAsync(
            string login,
            CancellationToken ct
        ) =>
            Task.FromResult<CustomCommandViewerResolution>(
                new CustomCommandViewerResolution.Found(new("stable-viewer", login, "Viewer"))
            );
    }
}
