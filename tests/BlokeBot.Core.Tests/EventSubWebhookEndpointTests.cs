using System.Net;
using System.Text;
using BlokeBot.Core.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class EventSubWebhookEndpointTests
{
    [Test]
    public async Task Callback_IsAnonymousPostOnlyAntiforgeryFreeAndPreservesExactBodyAndHeaders()
    {
        var ingress = new RecordingIngress(new EventSubWebhookResult(200, "exact challenge"));
        await using var host = await EndpointHost.StartAsync(ingress);
        var body = Encoding.UTF8.GetBytes("{\"exact\":\"\\u00A3\"}\r\n");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/eventsub/twitch")
        {
            Content = new ByteArrayContent(body),
        };
        request.Headers.Add("Twitch-Eventsub-Message-Id", "message-id");
        request.Headers.Add("Twitch-Eventsub-Message-Type", "webhook_callback_verification");
        request.Headers.Add("Twitch-Eventsub-Message-Timestamp", "2026-08-03T12:00:00Z");
        request.Headers.Add("Twitch-Eventsub-Message-Signature", "sha256=signature");
        request.Headers.Add("Twitch-Eventsub-Subscription-Type", "channel.chat.message");
        request.Headers.Add("Twitch-Eventsub-Subscription-Version", "1");

        using var response = await host.Client.SendAsync(request);
        using var get = await host.Client.GetAsync("/eventsub/twitch");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await response.Content.ReadAsStringAsync()).ShouldBe("exact challenge");
        get.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        ingress.Calls.ShouldBe(1);
        ingress.Body.ShouldBe(body);
        ingress.MessageId.ShouldBe("message-id");
        ingress.Signature.ShouldBe("sha256=signature");
    }

    [Test]
    public async Task Callback_ContentLengthAboveLimit_IsRejectedBeforeIngress()
    {
        var ingress = new RecordingIngress(new EventSubWebhookResult(202));
        await using var host = await EndpointHost.StartAsync(ingress);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/eventsub/twitch")
        {
            Content = new ByteArrayContent(new byte[(512 * 1024) + 1]),
        };

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        ingress.Calls.ShouldBe(0);
    }

    private sealed class RecordingIngress(EventSubWebhookResult result) : IEventSubWebhookIngress
    {
        internal int Calls { get; private set; }

        internal byte[] Body { get; private set; } = [];

        internal string? MessageId { get; private set; }

        internal string? Signature { get; private set; }

        public ValueTask<EventSubWebhookResult> HandleAsync(
            string? messageId,
            string? messageType,
            string? timestamp,
            string? signature,
            string? subscriptionType,
            string? subscriptionVersion,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Body = body.ToArray();
            MessageId = messageId;
            Signature = signature;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EndpointHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        internal HttpClient Client { get; } = client;

        internal static async Task<EndpointHost> StartAsync(IEventSubWebhookIngress ingress)
        {
            var builder = WebApplication.CreateBuilder();
            _ = builder.Services.AddSingleton(ingress);
            _ = builder.Services.AddAntiforgery();
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            _ = app.UseAntiforgery();
            app.MapEventSubWebhookEndpoint();
            await app.StartAsync();
            var address =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException("The test host did not publish an address.");
            return new(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
