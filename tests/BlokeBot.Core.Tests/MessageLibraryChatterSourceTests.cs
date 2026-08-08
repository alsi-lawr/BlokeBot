using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MessageLibraryChatterSourceTests
{
    [Test]
    public async Task CompleteSnapshot_UsesActiveBotAuthorityExcludesBotAndCachesForOneMinute()
    {
        var provider = new RecordingTokenStatusProvider(Ready("bot-id", "secret-token"));
        var http = new RecordingHttpClientFactory();
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero)
        );
        var source = new MessageLibraryChatterSource(
            provider,
            new HelixClient(http, TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            clock
        );
        var host = new MessageLibraryRenderHost(7, "streamer", "channel-id");

        var first = await source.GetAsync(host, CancellationToken.None);
        var cached = await source.GetAsync(host, CancellationToken.None);

        first.Select(static chatter => chatter.DisplayName).ShouldBe(["Viewer"]);
        cached.ShouldBe(first);
        provider
            .RequiredScopes.Select(static scopes => scopes.Single())
            .ShouldBe([Scopes.ModeratorReadChatters, Scopes.ModeratorReadChatters]);
        http.RequestCount.ShouldBe(1);
        http.ModeratorIds.ShouldBe(["bot-id"]);
        http.AccessTokens.ShouldBe(["secret-token"]);

        clock.Advance(TimeSpan.FromSeconds(60));
        _ = await source.GetAsync(host, CancellationToken.None);
        http.RequestCount.ShouldBe(2);
    }

    [Test]
    public async Task ActiveBotIdentityChanges_MissesAndReplacesCachedSnapshot()
    {
        var provider = new RecordingTokenStatusProvider(Ready("first-id", "first-token"));
        var http = new RecordingHttpClientFactory();
        var source = new MessageLibraryChatterSource(
            provider,
            new HelixClient(http, TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
            ),
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero))
        );
        var host = new MessageLibraryRenderHost(7, "streamer", "channel-id");

        _ = await source.GetAsync(host, CancellationToken.None);
        provider.Status = Ready("second-id", "second-token");
        _ = await source.GetAsync(host, CancellationToken.None);

        http.ModeratorIds.ShouldBe(["first-id", "second-id"]);
        http.AccessTokens.ShouldBe(["first-token", "second-token"]);
    }

    private static ActiveBotAccountTokenStatus Ready(string userId, string accessToken)
    {
        var scopes = ImmutableArray.Create(Scopes.ModeratorReadChatters);
        return new()
        {
            BotLogin = "bot",
            Status = new TokenStatus.Ready(
                accessToken,
                new(userId, "bot", OAuthScopeSet.Create(scopes)),
                scopes,
                scopes
            ),
        };
    }

    private sealed class RecordingTokenStatusProvider(ActiveBotAccountTokenStatus status)
        : IHostBotAccountTokenStatusProvider
    {
        public ActiveBotAccountTokenStatus Status { get; set; } = status;

        public List<string[]> RequiredScopes { get; } = [];

        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        )
        {
            RequiredScopes.Add(requiredScopes.OfType<string>().ToArray());
            return Task.FromResult(Status);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public int RequestCount { get; private set; }

        public List<string> ModeratorIds { get; } = [];

        public List<string> AccessTokens { get; } = [];

        public HttpClient CreateClient(string name) =>
            new(new Handler(this), disposeHandler: false);

        private sealed class Handler(RecordingHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.RequestCount++;
                request.RequestUri!.AbsolutePath.ShouldBe("/helix/chat/chatters");
                request.RequestUri.Query.ShouldContain("broadcaster_id=channel-id");
                owner.ModeratorIds.Add(QueryValue(request.RequestUri, "moderator_id"));
                request.Headers.GetValues("Client-Id").Single().ShouldBe("client-id");
                owner.AccessTokens.Add(request.Headers.Authorization!.Parameter!);
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""
                            {
                              "data": [
                                {"user_id":"{{owner.ModeratorIds[
                                ^1
                            ]}}","user_login":"bot","user_name":"Bot"},
                                {"user_id":"viewer-id","user_login":"viewer","user_name":"Viewer"}
                              ],
                              "pagination": {}
                            }
                            """,
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }

            private static string QueryValue(Uri uri, string key) =>
                uri
                    .Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(static part => part.Split('=', 2))
                    .Where(parts =>
                        parts.Length == 2
                        && string.Equals(
                            Uri.UnescapeDataString(parts[0]),
                            key,
                            StringComparison.Ordinal
                        )
                    )
                    .Select(static parts => Uri.UnescapeDataString(parts[1]))
                    .Single();
        }
    }
}
