using System.Net;
using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Plugins.Features;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Transfer_RealModeratorAccessAllowsCommitAndRevocationDuringStagingRejectsWithoutPartialState()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var access = new HostModAccessService(
            fixture.Database,
            new HostedChannelChangeNotifier(fixture.Events)
        );
        var moderator = new ModeratorAuthorityService(
            new TransferAppToken(),
            new HelixClient(
                new TransferModeratorHttp($"host-{fixture.OtherHostId}"),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "fixture-client" } }
            ),
            access,
            TimeProvider.System
        );
        var authority = new OverlayManagementAuthority(fixture.Database, moderator);
        var service = new FullOverlayService(
            fixture.Database,
            authority,
            new CryptographicOverlayAccessKeyGenerator(),
            new(new EphemeralDataProtectionProvider()),
            fixture.Admission,
            fixture.Events,
            TimeProvider.System
        );
        var transfer = new FullOverlayTransferService(
            authority,
            environment.Media,
            service,
            new(new PluginFeatureDeclarationRegistry()),
            NullLogger<FullOverlayTransferService>.Instance
        );
        var package = await Export(transfer, fixture.Owner, Document());
        var session = Session(fixture.OtherHostId, AuthRole.Moderator);
        var imported = Value(await transfer.ImportAsync(session, new MemoryStream(package), _ct));
        Value(await service.GetAsync(session, imported.Created.Overlay.Id, _ct))
            .Revision.Value.ShouldBe(1);
        _ = (
            await moderator.AuthorizeAsync(session, fixture.HostId, _ct)
        ).ShouldBeOfType<ModeratorAuthorityOutcome.HostMismatch>();
        var changed = false;
        using var staged = new TransferReadStream(
            package,
            () =>
            {
                if (changed)
                {
                    return;
                }
                changed = true;
                access
                    .DisableModeratorAccessAsync(fixture.OtherHostId, _ct)
                    .GetAwaiter()
                    .GetResult();
            }
        );
        Rejected(
            await transfer.ImportAsync(session, staged, _ct),
            FullOverlayRejectionKind.Unauthorized
        );
        changed.ShouldBeTrue();
        _ = (
            await moderator.AuthorizeAsync(session, fixture.OtherHostId, _ct)
        ).ShouldBeOfType<ModeratorAuthorityOutcome.Revoked>();
        await using var db = fixture.Database.CreateDbContext();
        (await db.FullOverlays.CountAsync()).ShouldBe(1);
        (await db.FullOverlays.SingleAsync()).HostId.ShouldBe(fixture.OtherHostId);
        (await db.OverlayMediaAssets.CountAsync()).ShouldBe(0);
        Directory.GetFiles(environment.MediaRoot).ShouldBeEmpty();
    }

    private sealed class TransferAppToken : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("fixture-app-token");
    }

    private sealed class TransferModeratorHttp(string allowedLogin) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(allowedLogin));

        private sealed class Handler(string allowedLogin) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(
                                new
                                {
                                    data = new[] { new { broadcaster_login = allowedLogin } },
                                    pagination = new { },
                                }
                            )
                        ),
                    }
                );
        }
    }
}
