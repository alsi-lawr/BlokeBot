using System.Security.Claims;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using Bunit;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public Task EditorDelayedReleasePreservesNewerSameRevisionReplayAndReclaimsItsOwnStaleCandidate() =>
        EditorInstallationRaceAsync(InstallationRace.Replay);

    [Test]
    public Task EditorDelayedReleaseRejectsChangedSourceAndStaleSameRevisionNotifications() =>
        EditorInstallationRaceAsync(InstallationRace.SourceEdit);

    [Test]
    public Task EditorDelayedReleaseCannotInstallAfterPageDisposal() =>
        EditorInstallationRaceAsync(InstallationRace.Disposal);

    private static async Task EditorInstallationRaceAsync(InstallationRace race)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var gate = new DelayedModerator();
        await using var delivery = new FullOverlayDelivery(
            fixture.Reader,
            fixture.Service,
            new(fixture.Database, gate),
            environment.Registry,
            runtime.Cues,
            environment.Media,
            runtime.Live,
            environment.FeedChanges,
            environment.Clock,
            NullLogger<FullOverlayDelivery>.Instance
        );
        var created = await fixture.CreateOverlayAsync();
        var session = Session(fixture.HostId, AuthRole.Moderator);
        var ui = UiTestContextFactory.CreateWithAuthorization(fixture.Database, fixture.HostId);
        using var context = ui.Context;
        var host = new BotHostChoice(
            fixture.HostId,
            $"host-{fixture.HostId}",
            "Host",
            AuthRole.Moderator
        );
        _ = ui.Authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "author-id"),
            new Claim(ClaimTypes.Name, "author"),
            new Claim(AuthClaims.Login, "author"),
            new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Moderator)),
            new Claim(AuthClaims.CanCreateHost, "false"),
            new Claim(AuthClaims.IsBotAdmin, "false"),
            new Claim(AuthClaims.IsBotAccount, "false"),
            new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(host)),
            new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(host))
        );
        _ = context
            .Services.AddBlokeBotPlayWithViewers()
            .AddBlokeBotCommunityProgression()
            .AddBlokeBotBounties();
        _ = context.Services.AddSingleton(fixture.Service);
        _ = context.Services.AddSingleton(environment.Registry);
        _ = context.Services.AddSingleton(environment.Media);
        _ = context.Services.AddSingleton(delivery);
        var module = context.JSInterop.SetupModule(
            "/Features/Overlays/Full/Editor/EditorClient.js"
        );
        var client = module.SetupModule("createClient", _ => true);
        _ = client
            .Setup<IJSStreamReference>("candidateStream", _ => true)
            .SetResult(new CandidateStream(created.Overlay.Draft));
        var installs = client.SetupVoid("preview", _ => true).SetVoidResult();
        var page = context.Render<FullOverlayEditorPage>(parameters =>
            parameters.Add(component => component.OverlayId, created.Overlay.Id)
        );
        page.WaitForAssertion(() => installs.Invocations.Count.ShouldBe(1));
        var initial = Guid.Parse((string)installs.Invocations.First().Arguments[0]!);
        var stale = Value(
            await delivery.CreatePreviewAsync(
                session,
                created.Overlay.Draft,
                FullOverlayDataMode.Sample,
                _ct
            )
        );
        gate.PauseNext = true;
        Task<bool> pending = null!;
        await page.InvokeAsync(() =>
        {
            pending = page.Instance.InstallPreviewAsync(stale, 1, 0);
        });
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Guid? latest = null;
        switch (race)
        {
            case InstallationRace.Replay:
                await page.InvokeAsync(() => page.Instance.RefreshPreviewAsync(0));
                latest = Guid.Parse((string)installs.Invocations.Last().Arguments[0]!);
                latest.ShouldNotBe(initial);
                break;
            case InstallationRace.SourceEdit:
                await page.InvokeAsync(() =>
                    page.Instance.EditorChangedAsync(
                        FullOverlayEditorView.Empty with
                        {
                            Revision = 1,
                            Dirty = true,
                            Focus = "css",
                        },
                        2
                    )
                );
                await page.InvokeAsync(() =>
                    page.Instance.EditorChangedAsync(
                        FullOverlayEditorView.Empty with
                        {
                            Revision = 1,
                            Focus = "html",
                        },
                        1
                    )
                );
                page.FindComponent<FullOverlayInspector>().Instance.View.Focus.ShouldBe("css");
                page.FindComponent<NavigationLock>()
                    .Instance.ConfirmExternalNavigation.ShouldBeTrue();
                break;
            case InstallationRace.Disposal:
                await page.InvokeAsync(() => page.Instance.DisposeAsync().AsTask());
                break;
        }
        gate.Continue.SetResult();
        (await pending.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeFalse();
        (await delivery.MayOpenPreviewAsync(stale, session, _ct)).ShouldBeFalse();
        (await delivery.MayOpenPreviewAsync(initial, session, _ct)).ShouldBeFalse();
        installs
            .Invocations.Select(invocation => (string)invocation.Arguments[0]!)
            .ShouldNotContain(stale.ToString());
        if (race == InstallationRace.SourceEdit)
        {
            await page.InvokeAsync(() => page.Instance.RefreshPreviewAsync(1));
            latest = Guid.Parse((string)installs.Invocations.Last().Arguments[0]!);
        }
        if (latest is { } retained)
        {
            (await delivery.MayOpenPreviewAsync(retained, session, _ct)).ShouldBeTrue();
            Guid.Parse((string)installs.Invocations.Last().Arguments[0]!).ShouldBe(retained);
            await page.InvokeAsync(() => page.Instance.DisposeAsync().AsTask());
            (await delivery.MayOpenPreviewAsync(retained, session, _ct)).ShouldBeFalse();
        }
        else
        {
            installs.Invocations.Count.ShouldBe(1);
        }
    }

    private enum InstallationRace
    {
        Replay,
        SourceEdit,
        Disposal,
    }

    private sealed class DelayedModerator : IModeratorAuthorityService
    {
        internal bool PauseNext { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        )
        {
            if (PauseNext)
            {
                PauseNext = false;
                Entered.SetResult();
                await Continue.Task.WaitAsync(ct);
            }
            return new ModeratorAuthorityOutcome.Granted();
        }
    }

    private sealed class CandidateStream(FullOverlayDocument document) : IJSStreamReference
    {
        private readonly byte[] _json = System.Text.Encoding.UTF8.GetBytes(
            FullOverlayDocuments.Serialize(document)
        );
        public long Length => _json.Length;

        public ValueTask<Stream> OpenReadStreamAsync(
            long maxAllowedSize = 512000,
            CancellationToken cancellationToken = default
        ) => new(new MemoryStream(_json));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
