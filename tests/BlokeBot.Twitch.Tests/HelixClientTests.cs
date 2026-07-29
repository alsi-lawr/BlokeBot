using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Twitch;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Tests;

public sealed class HelixClientTests
{
    [Test]
    public async Task LiveStreamPayload_LoadingStream_ReturnsTwitchStreamId()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/streams");
            request.RequestUri.Query.ShouldContain("user_login=streamer");
            return JsonResponse(
                """
                {
                  "data": [
                    {
                      "id": "stream-id",
                      "user_id": "user-id",
                      "user_login": "streamer",
                      "user_name": "Streamer",
                      "game_id": "game-id",
                      "game_name": "Example Game",
                      "type": "live",
                      "title": "Representative stream",
                      "tags": ["English", "Casual"],
                      "viewer_count": 42,
                      "started_at": "2026-07-13T12:34:56Z",
                      "language": "en",
                      "thumbnail_url": "https://example.test/{width}x{height}.jpg",
                      "is_mature": false
                    }
                  ],
                  "pagination": {}
                }
                """
            );
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var stream = await client.GetStreamAsync(Context(), "Streamer", CancellationToken.None);

        stream.ShouldNotBeNull().Id.ShouldBe("stream-id");
    }

    [Test]
    public async Task EmptyStreamPayload_CheckingStreamStatus_ReturnsOffline()
    {
        var factory = RespondingWith("""{"data":[],"pagination":{}}""");
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var isLive = await client.IsStreamLiveAsync(Context(), "streamer", CancellationToken.None);

        isLive.ShouldBeFalse();
    }

    [Test]
    public async Task ChannelInformation_LoadingRaiderMetadata_ReturnsTypedGameAndTitle()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/channels");
            request.RequestUri.Query.ShouldBe("?broadcaster_id=raider-id");
            return JsonResponse(
                """{"data":[{"broadcaster_id":"raider-id","game_name":"Last Game","title":"Last stream title"}]}"""
            );
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var result = await client.GetChannelInformationAsync(
            Context(),
            "raider-id",
            CancellationToken.None
        );

        var found = result.ShouldBeOfType<HelixChannelInformationOutcome.Found>();
        found.GameName.ShouldBe("Last Game");
        found.Title.ShouldBe("Last stream title");
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden, typeof(HelixChannelInformationOutcome.PermissionDenied))]
    [Arguments(
        HttpStatusCode.InternalServerError,
        typeof(HelixChannelInformationOutcome.Unavailable)
    )]
    public async Task ChannelInformation_FailedRead_ReturnsTypedFailure(
        HttpStatusCode status,
        Type expectedType
    )
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(_ => new HttpResponseMessage(status));
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var result = await client.GetChannelInformationAsync(
            Context(),
            "raider-id",
            CancellationToken.None
        );

        result.GetType().ShouldBe(expectedType);
    }

    [Test]
    public async Task FollowerPayload_CheckingFollowerStatus_ReturnsFollows()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/channels/followers");
            request.RequestUri.Query.ShouldContain("broadcaster_id=broadcaster-id");
            request.RequestUri.Query.ShouldContain("moderator_id=moderator-id");
            request.RequestUri.Query.ShouldContain("user_id=user-id");
            return JsonResponse(
                """
                {
                  "total": 8,
                  "data": [
                    {
                      "user_id": "user-id",
                      "user_login": "viewer",
                      "user_name": "Viewer",
                      "followed_at": "2026-07-12T11:22:33Z"
                    }
                  ],
                  "pagination": {}
                }
                """
            );
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var status = await client.GetFollowerStatusAsync(
            Context(),
            "broadcaster-id",
            "user-id",
            "moderator-id",
            CancellationToken.None
        );

        status.ShouldBeOfType<FollowerStatus.Follows>();
    }

    [Test]
    public async Task EmptyFollowerPayload_CheckingFollowerStatus_ReturnsDoesNotFollow()
    {
        var factory = RespondingWith("""{"total":8,"data":[],"pagination":{}}""");
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var status = await client.GetFollowerStatusAsync(
            Context(),
            "broadcaster-id",
            "user-id",
            "moderator-id",
            CancellationToken.None
        );

        status.ShouldBeOfType<FollowerStatus.DoesNotFollow>();
    }

    [Test]
    public async Task ChatSettingsPayload_LoadingWithAppToken_ParsesFollowerModeAndDuration()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/chat/settings");
            request.RequestUri.Query.ShouldBe("?broadcaster_id=broadcaster-id");
            request.Headers.Authorization!.Parameter.ShouldBe("app-token");
            request.Headers.GetValues("Client-Id").Single().ShouldBe("client");
            return JsonResponse(
                """
                {
                  "data": [
                    {"follower_mode":true,"follower_mode_duration":15}
                  ]
                }
                """
            );
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var settings = await client.GetChatSettingsAsync(
            new HelixRequestContext("client", "app-token"),
            "broadcaster-id",
            CancellationToken.None
        );

        settings.FollowerMode.ShouldBeTrue();
        settings.FollowerModeDuration.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Test]
    public async Task FollowedChannelPayload_CheckingActiveBotFollowStatus_UsesDirectActorQuery()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/channels/followed");
            request.RequestUri.Query.ShouldContain("user_id=validated-bot-subject");
            request.RequestUri.Query.ShouldContain("broadcaster_id=channel-id");
            request.RequestUri.Query.ShouldNotContain("moderator_id=");
            request.Headers.Authorization!.Parameter.ShouldBe("bot-token");
            return JsonResponse(
                """
                {
                  "data": [
                    {
                      "user_id":"validated-bot-subject",
                      "user_login":"bot",
                      "user_name":"Bot",
                      "followed_at":"2026-07-18T11:30:00Z"
                    }
                  ]
                }
                """
            );
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var status = await client.GetFollowedChannelStatusAsync(
            new HelixRequestContext("client", "bot-token"),
            "validated-bot-subject",
            "channel-id",
            CancellationToken.None
        );

        status
            .ShouldBeOfType<ActiveBotFollowStatus.Follows>()
            .FollowedAtUtc.ShouldBe(new DateTimeOffset(2026, 7, 18, 11, 30, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task StreamItemMissingRequiredField_CheckingStreamStatus_RejectsPayload()
    {
        var factory = RespondingWith(
            """
            {
              "data": [
                {
                  "user_id": "user-id",
                  "user_login": "streamer",
                  "user_name": "Streamer",
                  "game_id": "game-id",
                  "game_name": "Example Game",
                  "type": "live",
                  "title": "Representative stream",
                  "tags": [],
                  "viewer_count": 42,
                  "started_at": "2026-07-13T12:34:56Z",
                  "language": "en",
                  "thumbnail_url": "https://example.test/{width}x{height}.jpg",
                  "is_mature": false
                }
              ]
            }
            """
        );
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        await Should.ThrowAsync<JsonException>(() =>
            client.IsStreamLiveAsync(Context(), "streamer", CancellationToken.None)
        );
    }

    [Test]
    public async Task FollowerItemMissingRequiredField_CheckingFollowerStatus_RejectsPayload()
    {
        var factory = RespondingWith(
            """
            {
              "data": [
                {
                  "user_id": "user-id",
                  "user_login": "viewer",
                  "user_name": "Viewer"
                }
              ]
            }
            """
        );
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        await Should.ThrowAsync<JsonException>(() =>
            client.GetFollowerStatusAsync(
                Context(),
                "broadcaster-id",
                "user-id",
                "moderator-id",
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task AcceptedShoutout_SendingThroughHelix_MapsToSent()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/chat/shoutouts");
            request.RequestUri.Query.ShouldContain("from_broadcaster_id=source-id");
            request.RequestUri.Query.ShouldContain("to_broadcaster_id=target-id");
            request.RequestUri.Query.ShouldContain("moderator_id=bot-id");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var result = await client.SendShoutoutAsync(
            Context(),
            "source-id",
            "bot-id",
            "target-id",
            CancellationToken.None
        );

        result.ShouldBeOfType<ShoutoutSendResult.Sent>();
    }

    [Test]
    public async Task PaginatedModeratedChannels_LoadingThroughHelix_ReturnsAllPagesWithAuth()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            request.Headers.Authorization.Parameter.ShouldBe("token");
            request.Headers.GetValues("Client-Id").Single().ShouldBe("client");
            request.RequestUri!.Query.ShouldContain("first=100");
            request.RequestUri.Query.ShouldContain("user_id=bot-id");
            return JsonResponse(
                """
                {
                  "data": [
                    {"broadcaster_id":"1","broadcaster_login":"one","broadcaster_name":"One"}
                  ],
                  "pagination": {"cursor":"next"}
                }
                """
            );
        });
        factory.Respond(request =>
        {
            request.RequestUri!.Query.ShouldContain("after=next");
            return JsonResponse(
                """
                {
                  "data": [
                    {"broadcaster_id":"2","broadcaster_login":"two","broadcaster_name":"Two"}
                  ],
                  "pagination": {}
                }
                """
            );
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var channels = await client.GetModeratedChannelsAsync(
            new HelixRequestContext("client", "token"),
            "bot-id",
            CancellationToken.None
        );

        channels.Select(channel => channel.BroadcasterLogin).ShouldBe(["one", "two"]);
    }

    [Test]
    public async Task PollRequests_HelixGetCreateAndEnd_MapRequestsAndResponses()
    {
        const string ActivePoll = """
            {"data":[{"id":"poll-id","broadcaster_id":"broadcaster-id","title":"Question","choices":[{"id":"one","title":"Yes","votes":2,"channel_points_votes":1},{"id":"two","title":"No","votes":1,"channel_points_votes":0}],"status":"ACTIVE","started_at":"2026-07-26T10:00:00Z","ends_at":"2026-07-26T10:02:00Z"}]}
            """;
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/polls");
            request.RequestUri.Query.ShouldContain("broadcaster_id=broadcaster-id");
            request.RequestUri.Query.ShouldContain("first=1");
            return JsonResponse(ActivePoll);
        });
        factory.Respond(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/polls");
            request
                .Content!.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult()
                .ShouldBe(
                    "{\"broadcaster_id\":\"broadcaster-id\",\"title\":\"Question\",\"choices\":[{\"title\":\"Yes\"},{\"title\":\"No\"}],\"duration\":120,\"channel_points_voting_enabled\":true,\"channel_points_per_vote\":10}"
                );
            return JsonResponse(ActivePoll);
        });
        factory.Respond(request =>
        {
            request.Method.ShouldBe(HttpMethod.Patch);
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/polls");
            request
                .Content!.ReadAsStringAsync()
                .GetAwaiter()
                .GetResult()
                .ShouldBe(
                    "{\"broadcaster_id\":\"broadcaster-id\",\"id\":\"poll-id\",\"status\":\"TERMINATED\"}"
                );
            return JsonResponse(ActivePoll.Replace("ACTIVE", "TERMINATED"));
        });
        var client = new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var active = await client.GetLatestPollAsync(
            Context(),
            "broadcaster-id",
            CancellationToken.None
        );
        var created = await client.CreatePollAsync(
            Context(),
            "broadcaster-id",
            new HelixPollCreateRequest("Question", ["Yes", "No"], 120, true, 10),
            CancellationToken.None
        );
        var ended = await client.EndPollAsync(
            Context(),
            "broadcaster-id",
            "poll-id",
            HelixPollEndStatus.Terminated,
            CancellationToken.None
        );

        active
            .ShouldBeOfType<HelixPollLookupOutcome.Found>()
            .Poll.Choices[0]
            .ChannelPointsVotes.ShouldBe(1);
        created
            .ShouldBeOfType<HelixPollCreateOutcome.Created>()
            .Poll.Status.ShouldBe(HelixPollStatus.Active);
        ended.ShouldNotBeNull().Status.ShouldBe(HelixPollStatus.Terminated);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static HelixRequestContext Context()
    {
        return new("client", "token");
    }

    private static ScriptedHttpClientFactory RespondingWith(string json)
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(_ => JsonResponse(json));
        return factory;
    }

    private sealed class ScriptedHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public void Respond(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _responses.Enqueue(response);
        }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(_responses), disposeHandler: false);
        }

        private sealed class Handler(Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                responses.Count.ShouldBeGreaterThan(0);
                return Task.FromResult(responses.Dequeue()(request));
            }
        }
    }
}
