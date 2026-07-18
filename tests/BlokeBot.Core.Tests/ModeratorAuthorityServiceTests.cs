using System.Net;
using System.Text;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class ModeratorAuthorityServiceTests
{
    [Test]
    public async Task ModeratorWithPriorAuthorization_CheckingAuthority_UsesSessionActorAndAppToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Helix.Respond(request =>
        {
            request.RequestUri!.Query.ShouldContain("user_id=moderator-id");
            request.Headers.Authorization!.Parameter.ShouldBe("app-token");
            request.Headers.GetValues("Client-Id").Single().ShouldBe("client-id");
            return JsonResponse("""{"data":[{"broadcaster_login":"streamer"}],"pagination":{}}""");
        });

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(AuthRole.Moderator, fixture.HostId),
            fixture.HostId,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        fixture.Tokens.RequestCount.ShouldBe(1);
        fixture.Helix.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task DefinitiveAuthority_CheckingBeforeAndAtExpiry_UsesPerUserHostCacheForFifteenMinutes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Helix.Respond(_ => AllowedResponse());
        fixture.Helix.Respond(_ => AllowedResponse());
        fixture.Helix.Respond(_ => AllowedResponse());

        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        fixture.Time.Advance(TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(59)));
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId, userId: "another-moderator-id"),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();

        fixture.Helix.RequestCount.ShouldBe(2);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();

        fixture.Helix.RequestCount.ShouldBe(3);
    }

    [Test]
    public async Task MissingModeratedChannel_CheckingAuthority_ConfirmsRevocationAndCachesTheDenial()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Helix.Respond(_ => JsonResponse("""{"data":[],"pagination":{}}"""));

        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Revoked>();
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Revoked>();

        fixture.Helix.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task ProviderUncertainty_CheckingAuthority_DeniesWithoutCaching()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tokens.Exception = new HttpRequestException("offline");

        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Unavailable>();
        (
            await fixture.Service.AuthorizeAsync(
                Session(AuthRole.Moderator, fixture.HostId),
                fixture.HostId,
                CancellationToken.None
            )
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Unavailable>();

        fixture.Tokens.RequestCount.ShouldBe(2);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    [Test]
    [Arguments(AuthRole.Streamer)]
    [Arguments(AuthRole.Admin)]
    public async Task BroadcasterAndAdministrator_CheckingAuthority_BypassProvider(AuthRole role)
    {
        await using var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(role, fixture.HostId),
            fixture.HostId,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.Granted>();
        fixture.Tokens.RequestCount.ShouldBe(0);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task DifferentRequestedHost_CheckingAuthority_DeniesWithoutProviderOrRevocation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(AuthRole.Moderator, fixture.HostId),
            fixture.HostId + 1,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.HostMismatch>();
        fixture.Tokens.RequestCount.ShouldBe(0);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task DisabledModeratorConfigurePolicy_CheckingAuthority_RevokesWithoutProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ModeratorAccess.DisableModeratorAccessAsync(
            fixture.HostId,
            CancellationToken.None
        );

        var outcome = await fixture.Service.AuthorizeAsync(
            Session(AuthRole.Moderator, fixture.HostId),
            fixture.HostId,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ModeratorAuthorityOutcome.Revoked>();
        fixture.Tokens.RequestCount.ShouldBe(0);
        fixture.Helix.RequestCount.ShouldBe(0);
    }

    private static AuthenticatedSession Session(
        AuthRole role,
        int hostId,
        string userId = "moderator-id"
    )
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = userId,
            Login = "moderator",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static HttpResponseMessage AllowedResponse()
    {
        return JsonResponse("""{"data":[{"broadcaster_login":"streamer"}],"pagination":{}}""");
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteBlokeBotDbFactory database,
            ManualTimeProvider time,
            FakeAppAccessTokenSource tokens,
            ScriptedHttpClientFactory helix,
            ModeratorAuthorityService service,
            HostModAccessService moderatorAccess,
            int hostId
        )
        {
            Database = database;
            Time = time;
            Tokens = tokens;
            Helix = helix;
            Service = service;
            ModeratorAccess = moderatorAccess;
            HostId = hostId;
        }

        public SqliteBlokeBotDbFactory Database { get; }
        public ManualTimeProvider Time { get; }
        public FakeAppAccessTokenSource Tokens { get; }
        public ScriptedHttpClientFactory Helix { get; }
        public ModeratorAuthorityService Service { get; }
        public HostModAccessService ModeratorAccess { get; }
        public int HostId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            await using (var db = await database.CreateDbContextAsync())
            {
                db.Hosts.Add(
                    new()
                    {
                        Login = "streamer",
                        DisplayName = "Streamer",
                        CreatedAtUtc = DateTime.UtcNow,
                    }
                );
                await db.SaveChangesAsync();
            }

            await using var lookup = await database.CreateDbContextAsync();
            var hostId = lookup.Hosts.Single().Id;
            var events = TestEventBus.Create<AppEventKind>();
            var time = new ManualTimeProvider(
                new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero)
            );
            var tokens = new FakeAppAccessTokenSource();
            var helix = new ScriptedHttpClientFactory();
            var moderatorAccess = new HostModAccessService(
                database,
                new HostedChannelChangeNotifier(events)
            );
            var service = new ModeratorAuthorityService(
                tokens,
                new HelixClient(helix),
                BotSettings.FromOptions(
                    new BotOptions { Identity = new BotIdentityOptions { ClientId = "client-id" } }
                ),
                moderatorAccess,
                time
            );
            return new Fixture(database, time, tokens, helix, service, moderatorAccess, hostId);
        }

        public ValueTask DisposeAsync()
        {
            return Database.DisposeAsync();
        }
    }

    private sealed class FakeAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        public Exception? Exception { get; set; }
        public int RequestCount { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            RequestCount++;
            if (Exception is { } exception)
            {
                return Task.FromException<string>(exception);
            }

            return Task.FromResult("app-token");
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan elapsed)
        {
            _now = _now.Add(elapsed);
        }
    }

    private sealed class ScriptedHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public int RequestCount { get; private set; }

        public void Respond(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _responses.Enqueue(response);
        }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(this));
        }

        private sealed class Handler(ScriptedHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.RequestCount++;
                return Task.FromResult(owner._responses.Dequeue()(request));
            }
        }
    }
}
