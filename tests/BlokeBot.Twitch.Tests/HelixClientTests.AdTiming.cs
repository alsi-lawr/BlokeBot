using System.Net;
using Shouldly;

namespace BlokeBot.Twitch.Tests;

public sealed partial class HelixClientTests
{
    [Test]
    public async Task AdSchedule_UsesBroadcasterObservationToken_AndReadsProviderDeadlineNotPastDuration()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.ShouldBe("/helix/channels/ads");
            request.RequestUri.Query.ShouldBe("?broadcaster_id=channel-id");
            request.Headers.Authorization!.Parameter.ShouldBe("broadcaster-read-token");
            request.Headers.GetValues("Client-Id").Single().ShouldBe("client");
            return JsonResponse(
                """{"data":[{"next_ad_at":"2026-10-03T20:30:00Z","last_ad_at":"","duration":"180"}]}"""
            );
        });
        var client = new HelixClient(factory, TwitchEndpointPolicy.Default);
        var result = (
            await client.GetAdScheduleAsync(
                new("client", "broadcaster-read-token"),
                "channel-id",
                CancellationToken.None
            )
        ).ShouldBeOfType<HelixAdScheduleOutcome.Available>();
        result.Schedule.NextAdAt.ShouldBe(
            new DateTimeOffset(2026, 10, 3, 20, 30, 0, TimeSpan.Zero)
        );
        result.Schedule.LastAdAt.ShouldBeNull();
        result.Schedule.DurationSeconds.ShouldBe(180);
    }

    [Test]
    [Arguments("bad JSON")]
    [Arguments("[]")]
    [Arguments("{\"data\":[null]}")]
    [Arguments("{\"data\":[{\"next_ad_at\":\"invalid\",\"last_ad_at\":\"\",\"duration\":180}]}")]
    [Arguments("{\"data\":[{\"next_ad_at\":\"\",\"last_ad_at\":\"\",\"duration\":-1}]}")]
    public async Task AdSchedule_MalformedObservation_IsUnavailableWithoutInventedDeadline(
        string payload
    )
    {
        var client = new HelixClient(RespondingWith(payload), TwitchEndpointPolicy.Default);
        _ = (
            await client.GetAdScheduleAsync(Context(), "channel", CancellationToken.None)
        ).ShouldBeOfType<HelixAdScheduleOutcome.Unavailable>();
    }

    [Test]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task AdSchedule_MissingObservationAuthority_IsExplicitlyUnauthorized(
        HttpStatusCode status
    )
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(_ => new HttpResponseMessage(status));
        var client = new HelixClient(factory, TwitchEndpointPolicy.Default);
        _ = (
            await client.GetAdScheduleAsync(Context(), "channel", CancellationToken.None)
        ).ShouldBeOfType<HelixAdScheduleOutcome.Unauthorized>();
    }
}
