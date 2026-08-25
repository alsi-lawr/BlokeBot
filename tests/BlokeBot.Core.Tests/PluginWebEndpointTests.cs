using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Core.Hosts;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginWebEndpointTests
{
    [Test]
    public async Task PublicAndCallbackAuthenticatedWebhooks_KeepRequestAndFailureBounds()
    {
        await using var host = await WebHost.StartAsync();
        using var publicResponse = await host.Client.PostAsync(
            Route("webhooks", "incoming", hostId: 1),
            new StringContent("hello")
        );
        publicResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await publicResponse.Content.ReadAsStringAsync()).ShouldBe("accepted");
        var invocation = host.Invoker.Webhooks.ShouldHaveSingleItem();
        invocation.Context.Host.Value.ShouldBe(1);
        invocation.Context.Web.ShouldBe(new(PluginWebInvocationKind.Webhook, "incoming", "POST"));
        var body = (
            (PluginValue.String)
                invocation.Input.Properties.Single(property => property.Name == "bodyBase64").Value
        ).Value;
        Convert.FromBase64String(body).ShouldBe("hello"u8.ToArray());

        host.Invoker.WebhookOutcome = new PluginWebDispatchOutcome.AuthenticationRejected();
        using var denied = await host.Client.PostAsync(
            Route("webhooks", "incoming", hostId: 1),
            new StringContent("secret")
        );
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await denied.Content.ReadAsStringAsync()).ShouldBeEmpty();

        using var oversized = await host.Client.PostAsync(
            Route("webhooks", "incoming", hostId: 1),
            new ByteArrayContent(new byte[PluginContractLimits.MaximumWebRequestBodyBytes + 1])
        );
        oversized.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        host.Invoker.Webhooks.Count.ShouldBe(2);
    }

    [Test]
    public async Task Actions_RequireTheExactAuthenticatedSelectedHostBeforeDispatch()
    {
        await using var host = await WebHost.StartAsync();
        using var unauthenticated = await host.Client.PostAsync(
            Route("actions", "refresh", hostId: 1),
            new StringContent("{}")
        );
        unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var wrongHostRequest = Request(Route("actions", "refresh", hostId: 2));
        using var wrongHost = await host.Client.SendAsync(wrongHostRequest);
        wrongHost.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        host.Invoker.Actions.ShouldBeEmpty();

        using var currentHostRequest = Request(Route("actions", "refresh", hostId: 1));
        using var currentHost = await host.Client.SendAsync(currentHostRequest);
        currentHost.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Invoker.Actions.ShouldHaveSingleItem().Context.Host.Value.ShouldBe(1);
    }

    private static HttpRequestMessage Request(string route)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent("{}"),
        };
        request.Headers.Add(TestAuthenticationHandler.HeaderName, "selected-host");
        return request;
    }

    private static string Route(string surface, string id, int hostId) =>
        $"/plugins/community.link-queue/hosts/{hostId}/features/collection/{surface}/{id}";

    private sealed class WebHost(WebApplication app, HttpClient client, RecordingWebInvoker invoker)
        : IAsyncDisposable
    {
        internal HttpClient Client { get; } = client;

        internal RecordingWebInvoker Invoker { get; } = invoker;

        internal static async Task<WebHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            var snapshot = Snapshot();
            var invoker = new RecordingWebInvoker();
            _ = builder.Services.AddSingleton<IPluginDispatchSnapshotProvider>(snapshot);
            _ = builder.Services.AddSingleton<IPluginDispatchInvoker>(invoker);
            _ = builder
                .Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    static _ => { }
                );
            _ = builder.Services.AddAuthorization(options =>
                options.AddPolicy("Operator", static policy => policy.RequireAuthenticatedUser())
            );

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();
            app.MapPluginWebEndpoints();
            await app.StartAsync();
            var address =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException("Plugin route host has no address.");
            return new(app, new HttpClient { BaseAddress = new(address) }, invoker);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }

        private static PluginDispatchSnapshotRegistry Snapshot()
        {
            var accepted = (
                (PluginManifestValidationOutcome.Accepted)
                    PluginManifestJson.Validate(
                        PluginContractFixtures.CompleteManifestJson(),
                        PluginContractFixtures.CompatibleHost()
                    )
            ).Manifest;
            var feature = accepted.Manifest.Features.Single(item => item.Id.Value == "collection");
            var module = accepted.Manifest.EntryModule;
            _ = PluginHostOperationId.TryCreate("handle", out var operation);
            _ = PluginWebhookId.TryCreate("incoming", out var webhook);
            _ = PluginActionId.TryCreate("refresh", out var action);
            var manifest = (
                (PluginManifestValidationOutcome.Accepted)
                    PluginManifestValidator.Validate(
                        accepted.Manifest with
                        {
                            Features = accepted.Manifest.Features.Replace(
                                feature,
                                feature with
                                {
                                    Dispatch = new(
                                        [],
                                        [],
                                        [],
                                        [
                                            new(
                                                webhook,
                                                module,
                                                operation,
                                                PluginCallbackRequirements.Independent,
                                                new PluginWebhookAuthentication.Public()
                                            ),
                                        ],
                                        [
                                            new(
                                                action,
                                                module,
                                                operation,
                                                PluginCallbackRequirements.Independent
                                            ),
                                        ]
                                    ),
                                }
                            ),
                        },
                        PluginContractFixtures.CompatibleHost()
                    )
            ).Manifest;
            var fence = new PluginLifecycleFence(PluginLifecycleOperationId.New(), Generation());
            var dispatch = new PluginDispatchSnapshotRegistry();
            dispatch.PublishDeclaration(
                new(new(manifest.Manifest.Id, manifest.Manifest.Release), fence, manifest.Manifest)
            );
            var features = new PluginFeatureSnapshotRegistry(dispatch);
            features.Hydrate(
                new[] { 1, 2 }.Select(hostId =>
                {
                    _ = PluginHostId.TryCreate(hostId, out var host);
                    _ = PluginFeatureGeneration.TryCreate(1, out var generation);
                    return new PluginFeatureState(
                        new(manifest.Manifest.Id, feature.Id, host),
                        fence,
                        generation,
                        new PluginFeatureReadiness.Ready(),
                        PluginFeatureRevision.Initial
                    );
                })
            );
            return dispatch;
        }

        private static PluginWorkerGeneration Generation()
        {
            _ = PluginWorkerGeneration.TryCreate(1, out var generation);
            return generation;
        }
    }

    private sealed class RecordingWebInvoker : IPluginDispatchInvoker
    {
        internal List<Invocation> Webhooks { get; } = [];

        internal List<Invocation> Actions { get; } = [];

        internal PluginWebDispatchOutcome WebhookOutcome { get; set; } =
            new PluginWebDispatchOutcome.Returned(
                new PluginValue.Map([
                    new("status", new PluginValue.Number(202)),
                    new("body", new PluginValue.String("accepted")),
                ])
            );

        public ValueTask<PluginDispatchInvocationOutcome> InvokeCommandAsync(
            PluginDispatchEndpoint.Command endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginDispatchInvocationOutcome> InvokeEventAsync(
            PluginDispatchEndpoint.Event endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginDispatchInvocationOutcome> InvokeScheduleAsync(
            PluginDispatchEndpoint.Schedule endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public ValueTask<PluginWebDispatchOutcome> InvokeWebhookAsync(
            PluginDispatchEndpoint.Webhook endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            Webhooks.Add(new(context, (PluginValue.Map)input));
            return ValueTask.FromResult(WebhookOutcome);
        }

        public ValueTask<PluginWebDispatchOutcome> InvokeActionAsync(
            PluginDispatchEndpoint.Action endpoint,
            PluginInvocationContext.Channel context,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            Actions.Add(new(context, (PluginValue.Map)input));
            return ValueTask.FromResult<PluginWebDispatchOutcome>(
                new PluginWebDispatchOutcome.Returned(new PluginValue.String("ok"))
            );
        }

        internal sealed record Invocation(
            PluginInvocationContext.Channel Context,
            PluginValue.Map Input
        );
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "PluginWebTest";
        internal const string HeaderName = "X-Plugin-Web-Test-Auth";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(HeaderName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var host = new BotHostChoice(1, "streamer", "Streamer", AuthRole.Streamer);
            var identity = new ClaimsIdentity(
                [
                    new(ClaimTypes.NameIdentifier, "streamer-id"),
                    new(ClaimTypes.Name, "Streamer"),
                    new(AuthClaims.Login, "streamer"),
                    new(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(host)),
                    new(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(host)),
                ],
                Scheme.Name
            );
            return Task.FromResult(
                AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name))
            );
        }
    }
}
