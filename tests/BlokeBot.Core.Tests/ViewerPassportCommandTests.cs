using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPassportCommandTests
{
    [Test]
    public async Task Summary_RespectsVisibilityAndFeatureGate()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "streamer-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.ViewerPassports,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var service = new ViewerPassportService(
            database,
            new PointBalanceService(database),
            TimeProvider.System
        );
        _ = Success(
            await service.SaveAsync(
                Save(hostId, ViewerPassportVisibility.Public, "PUBLIC-LINE"),
                default
            )
        );
        var services = new ServiceCollection();
        _ = services.AddSingleton(service);
        _ = services.AddChatCommands().AddCommandModule<ViewerPassportCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(dispatcher, responses);

        responses.Single().ShouldContain("Viewer: 0 points");
        responses.Single().ShouldContain("/passport/streamer/viewer");
        responses.Single().ShouldNotContain("PUBLIC-LINE");
        _ = Success(
            await service.SaveAsync(
                Save(hostId, ViewerPassportVisibility.Private, "PRIVATE-LINE"),
                default
            )
        );
        responses.Clear();

        await DispatchAsync(dispatcher, responses);

        responses.Single().ShouldBe("Open your viewer passport: /passports/streamer/me");
        responses.Single().ShouldNotContain("PRIVATE-LINE");
        _ = Success(
            await service.SaveAsync(
                Save(hostId, ViewerPassportVisibility.ChannelMembers, "MEMBERS-LINE"),
                default
            )
        );
        responses.Clear();

        await DispatchAsync(dispatcher, responses);

        responses.Single().ShouldBe("Open your viewer passport: /passports/streamer/me");
        responses.Single().ShouldNotContain("MEMBERS-LINE");
    }

    private static SaveViewerPassportCommand Save(
        int hostId,
        ViewerPassportVisibility visibility,
        string line
    ) => new(hostId, new("viewer-id", "viewer", "Viewer"), line, visibility, true, null, null);

    private static ViewerPassportView Success(ViewerPassportMutationOutcome outcome) =>
        outcome.ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>().Passport;

    private static async Task DispatchAsync(
        ChatCommandDispatcher dispatcher,
        List<string> responses
    ) =>
        await dispatcher.DispatchResponsesAsync(
            new ChatMessage(
                "viewer",
                "streamer",
                "!passport",
                "raw",
                new Dictionary<string, string>
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["user-id"] = "viewer-id",
                }
            ),
            (response, _) =>
            {
                responses.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            default
        );
}
