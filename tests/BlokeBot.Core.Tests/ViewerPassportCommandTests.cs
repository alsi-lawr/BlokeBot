using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            new LiveStreamProvider(),
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
        _ = services.AddSingleton(PublicSiteLinks("https://localhost/oauth/callback"));
        _ = services.AddChatCommands().AddCommandModule<ViewerPassportCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(dispatcher, responses);

        responses.Single().ShouldContain("Viewer: 0 points");
        responses.Single().ShouldContain("https://localhost/passport/streamer/viewer");
        responses.Single().ShouldNotContain("PUBLIC-LINE");
        _ = Success(
            await service.SaveAsync(
                Save(hostId, ViewerPassportVisibility.Private, "PRIVATE-LINE"),
                default
            )
        );
        responses.Clear();

        await DispatchAsync(dispatcher, responses);

        responses
            .Single()
            .ShouldBe("Open your viewer passport: https://localhost/passports/streamer/me");
        responses.Single().ShouldNotContain("PRIVATE-LINE");
        _ = Success(
            await service.SaveAsync(
                Save(hostId, ViewerPassportVisibility.ChannelMembers, "MEMBERS-LINE"),
                default
            )
        );
        responses.Clear();

        await DispatchAsync(dispatcher, responses);

        responses
            .Single()
            .ShouldBe("Open your viewer passport: https://localhost/passports/streamer/me");
        responses.Single().ShouldNotContain("MEMBERS-LINE");
    }

    private static SaveViewerPassportCommand Save(
        int hostId,
        ViewerPassportVisibility visibility,
        string line
    ) => new(hostId, new("viewer-id", "viewer", "Viewer"), line, visibility, true, null, null);

    private static ViewerPassportView Success(ViewerPassportMutationOutcome outcome) =>
        outcome.ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>().Passport;

    private sealed class LiveStreamProvider : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Live(
                            "command-test-stream",
                            DateTimeOffset.UnixEpoch
                        )
                    )
                )
            );
    }

    [Test]
    public async Task Summary_UsesTheConfiguredPublicBaseOrTheTwitchRedirectOrigin()
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
            new LiveStreamProvider(),
            TimeProvider.System
        );
        _ = Success(
            await service.SaveAsync(Save(hostId, ViewerPassportVisibility.Public, "LINE"), default)
        );

        async Task<string> ReplyAsync(string? publicBaseUrl, string redirectUri)
        {
            var services = new ServiceCollection();
            _ = services.AddSingleton(service);
            _ = services.AddSingleton(PublicSiteLinks(redirectUri, publicBaseUrl));
            _ = services.AddChatCommands().AddCommandModule<ViewerPassportCommandModule>();
            await using var provider = services.BuildServiceProvider();
            var responses = new List<string>();
            await DispatchAsync(provider.GetRequiredService<ChatCommandDispatcher>(), responses);
            return responses.Single();
        }

        (
            await ReplyAsync("https://bot.example.com", "http://localhost/oauth/callback")
        ).ShouldContain("https://bot.example.com/passport/streamer/viewer");
        (
            await ReplyAsync("https://bot.example.com/prefix", "http://localhost/oauth/callback")
        ).ShouldContain("https://bot.example.com/prefix/passport/streamer/viewer");
        (
            await ReplyAsync(null, "http://127.0.0.1:5080/oauth/callback?source=twitch#complete")
        ).ShouldContain("http://127.0.0.1:5080/passport/streamer/viewer");
    }

    [Test]
    [Arguments("not a url")]
    [Arguments("ftp://bot.example.com")]
    [Arguments("https://user:secret@bot.example.com")]
    [Arguments("https://bot.example.com?tenant=one")]
    [Arguments("https://bot.example.com#passport")]
    public void InvalidConfiguredPublicBase_FailsStartupOptionValidation(string publicBaseUrl) =>
        BlokeBotOptionsValidation
            .IsValid(new BlokeBotOptions { PublicBaseUrl = publicBaseUrl })
            .ShouldBeFalse();

    private static PublicSiteLinks PublicSiteLinks(
        string redirectUri,
        string? publicBaseUrl = null
    ) =>
        new(
            Options.Create(new BlokeBotOptions { PublicBaseUrl = publicBaseUrl }),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { RedirectUri = redirectUri } }
            )
        );

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
