using System.Net;
using System.Text;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationSourceAuthorizationTests
{
    [Test]
    public async Task AuthorizedSourceRepair_ExplicitSave_PreservesIdentityEnablementAndRawBindings()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.SelectRole(AuthRole.Moderator);
        await fixture.ChangeCandidateAsync(fixture.Candidate);
        await fixture.ValidateAsync();
        fixture.Source.Validated.ShouldBeTrue();

        await fixture.SaveAsync();

        var saved = await fixture.ReadStoredAsync();
        saved.Id.ShouldBe(fixture.Original.Id);
        saved.Name.ShouldBe("Authorized repair");
        saved.Enabled.ShouldBe(fixture.Original.Enabled);
        saved
            .Nodes.Select(node => node.Id)
            .ShouldBe(fixture.Original.Nodes.Select(node => node.Id));
        saved
            .Nodes.Select(node => node.Bindings)
            .ShouldBe(fixture.Original.Nodes.Select(node => node.Bindings));
        saved
            .Nodes.Select(node => node.Provenance)
            .ShouldBe(fixture.Original.Nodes.Select(node => node.Provenance));
        saved
            .Nodes.Single(node => node.Id == fixture.SendId)
            .Configuration.ShouldBe(Fixture.RepairedConfiguration);
        fixture.Http.RequestCount.ShouldBe(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PreviouslyGrantedModerator_RevokedBeforeSourceAction_RetainsCandidateAndOriginal(
        bool save
    )
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.SelectRole(AuthRole.Moderator);
        await fixture.ChangeCandidateAsync(fixture.Candidate);
        await fixture.ValidateAsync();
        fixture.Source.Validated.ShouldBeTrue();
        fixture.Http.RequestCount.ShouldBe(1);
        await fixture.Access.DisableModeratorAccessAsync(fixture.HostId, CancellationToken.None);

        if (save)
        {
            await fixture.SaveAsync();
        }
        else
        {
            await fixture.ValidateAsync();
        }

        await fixture.AssertOriginalAsync();
        fixture.Source.Text.ShouldBe(fixture.Candidate);
        fixture.Source.Validated.ShouldBeFalse();
        fixture.Http.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task SelectedHostChanged_SourceSave_DoesNotWriteCapturedHost()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ChangeCandidateAsync(fixture.Candidate);
        await fixture.ValidateAsync();
        fixture.Source.Validated.ShouldBeTrue();
        fixture.SelectRole(AuthRole.Streamer, fixture.OtherHostId);

        await fixture.SaveAsync();

        await fixture.AssertOriginalAsync();
        fixture.Source.Text.ShouldBe(fixture.Candidate);
        fixture.Source.Validated.ShouldBeFalse();
        fixture.Http.RequestCount.ShouldBe(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CandidateChangedDuringRealAuthorization_SourceAction_DoesNotSubmitOrApproveCapturedText(
        bool save
    )
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ChangeCandidateAsync(fixture.Candidate);
        if (save)
        {
            await fixture.ValidateAsync();
            fixture.Source.Validated.ShouldBeTrue();
        }
        fixture.SelectRole(AuthRole.Moderator);
        var pendingAuthority = fixture.Http.PauseNextRequest();
        var action = save ? fixture.SaveAsync() : fixture.ValidateAsync();
        await pendingAuthority.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await fixture.ChangeCandidateAsync("{");
        pendingAuthority.Release.SetResult();
        await action;

        await fixture.AssertOriginalAsync();
        fixture.Source.Text.ShouldBe("{");
        fixture.Source.Validated.ShouldBeFalse();
        fixture.Http.RequestCount.ShouldBe(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SelectedHostChangedDuringRealAuthorization_SourceAction_RetainsCapturedHostAndCandidate(
        bool save
    )
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ChangeCandidateAsync(fixture.Candidate);
        if (save)
        {
            await fixture.ValidateAsync();
            fixture.Source.Validated.ShouldBeTrue();
        }
        fixture.SelectRole(AuthRole.Moderator);
        var pendingAuthority = fixture.Http.PauseNextRequest();
        var action = save ? fixture.SaveAsync() : fixture.ValidateAsync();
        await pendingAuthority.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        fixture.SelectRole(AuthRole.Streamer, fixture.OtherHostId);
        pendingAuthority.Release.SetResult();
        await action;

        await fixture.AssertOriginalAsync();
        fixture.Source.Text.ShouldBe(fixture.Candidate);
        fixture.Source.Validated.ShouldBeFalse();
        fixture.Http.RequestCount.ShouldBe(1);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string RepairedConfiguration = " { \"message\" : \"tea\" } ";
        private readonly SqliteBlokeBotDbFactory _database;
        private readonly UiTestContextFactory.UiTestContext _ui;

        private Fixture(
            SqliteBlokeBotDbFactory database,
            UiTestContextFactory.UiTestContext ui,
            IRenderedComponent<AutomationEditorPage> page,
            HostModAccessService access,
            ControlledHttpClientFactory http,
            int hostId,
            int otherHostId,
            Guid flowId,
            Guid sendId,
            string candidate,
            StoredFlow original
        )
        {
            _database = database;
            _ui = ui;
            _page = page;
            Access = access;
            Http = http;
            HostId = hostId;
            OtherHostId = otherHostId;
            _flowId = flowId;
            SendId = sendId;
            Candidate = candidate;
            Original = original;
        }

        private IRenderedComponent<AutomationEditorPage> _page { get; }
        internal AutomationFlowSource Source =>
            _page.FindComponent<AutomationFlowSource>().Instance;
        internal HostModAccessService Access { get; }
        internal ControlledHttpClientFactory Http { get; }
        internal int HostId { get; }
        internal int OtherHostId { get; }
        private Guid _flowId { get; }
        internal Guid SendId { get; }
        internal string Candidate { get; }
        internal StoredFlow Original { get; }

        internal void SelectRole(AuthRole role, int? selectedHostId = null)
        {
            var host = new BotHostChoice(selectedHostId ?? HostId, "streamer", "Streamer", role);
            var principal = TestPrincipals.BlokeBotUser(
                "moderator",
                role: role,
                availableHosts: [host],
                selectedHost: host
            );
            _ = _ui.Authorization.SetClaims(principal.Claims.ToArray());
        }

        internal Task ChangeCandidateAsync(string text) =>
            _page.InvokeAsync(() => _page.Find("#automation-flow-json").Input(text));

        internal Task ValidateAsync() => _page.InvokeAsync(Source.Validate.InvokeAsync);

        internal Task SaveAsync() => _page.InvokeAsync(Source.Save.InvokeAsync);

        internal Task<StoredFlow> ReadStoredAsync() => ReadStoredAsync(_database, _flowId);

        internal async Task AssertOriginalAsync()
        {
            var current = await ReadStoredAsync();
            current.Id.ShouldBe(Original.Id);
            current.Name.ShouldBe(Original.Name);
            current.Enabled.ShouldBe(Original.Enabled);
            current.UpdatedAtUtc.ShouldBe(Original.UpdatedAtUtc);
            current.Nodes.ShouldBe(Original.Nodes);
            current.EdgeIds.ShouldBe(Original.EdgeIds);
        }

        internal static async Task<Fixture> CreateAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            int hostId;
            int otherHostId;
            await using (var db = await database.CreateDbContextAsync())
            {
                var host = new BotHost
                {
                    TwitchUserId = "source-repair-host",
                    Login = "streamer",
                    DisplayName = "Streamer",
                    EnabledFeatures = HostFeatureFlags.Automations,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                var other = new BotHost
                {
                    TwitchUserId = "source-repair-other",
                    Login = "other",
                    DisplayName = "Other",
                    EnabledFeatures = HostFeatureFlags.Automations,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                db.Hosts.AddRange(host, other);
                _ = await db.SaveChangesAsync();
                hostId = host.Id;
                otherHostId = other.Id;
            }

            var ui = UiTestContextFactory.CreateWithAuthorization(database, hostId);
            var services = ui.Context.Services;
            _ = services.AddSingleton<IOverlayCueAdmissionService>(
                new UnavailableOverlayCueAdmissionService()
            );
            _ = services.AddSingleton<IPublicChatMessageSender>(new RejectingChatSender());
            _ = services.AddBlokeBotAutomations();
            var http = new ControlledHttpClientFactory();
            _ = services.AddSingleton<IHttpClientFactory>(http);
            _ = services.AddSingleton<IHostBotAppAccessTokenSource>(new FixtureAppTokenSource());
            _ = services.AddSingleton(
                new HelixClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
            );
            _ = services.AddSingleton(
                BotSettings.FromOptions(
                    new BotOptions
                    {
                        Identity = new BotIdentityOptions { ClientId = "fixture-client" },
                    }
                )
            );
            _ = services.AddSingleton<HostModAccessService>();
            _ = services.AddSingleton<ModeratorAuthorityService>();

            var definitions = new CoreAutomationCatalogModule()
                .Definitions.Concat(new TwitchEventAutomationCatalogModule().Definitions)
                .Select(value => value.Descriptor)
                .ToArray();
            var editor = AutomationEditorState.Create("Malformed source fixture");
            var trigger = editor.AddNode(
                definitions.Single(value => value.Id == AutomationDefinitionIds.StreamOnlineSource)
            );
            var send = editor.AddNode(
                definitions.Single(value => value.Id == AutomationDefinitionIds.SendChatAction)
            );
            send.SetValue(new("message"), "tea");
            editor.Edges.Add(
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Flow,
                    trigger.Id,
                    trigger
                        .Definition.Outputs.Single(port =>
                            port.ValueType == AutomationPortValueType.Flow
                        )
                        .Id,
                    send.Id,
                    send.Definition.Inputs.Single(port =>
                        port.ValueType == AutomationPortValueType.Flow
                    ).Id
                )
            );
            var flows = services.GetRequiredService<AutomationFlowService>();
            var saved = (
                await flows.SaveAsync(editor.Draft(new(hostId)), CancellationToken.None)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            await using (var db = await database.CreateDbContextAsync())
            {
                var flow = await db.AutomationFlows.Include(value => value.Nodes).SingleAsync();
                flow.IsEnabled = true;
                flow.Nodes.Single(node => node.Id == send.Id.Value).ConfigurationJson =
                    "{ malformed nested source";
                _ = await db.SaveChangesAsync();
            }
            var read = (
                await flows.ReadForAuthoringAsync(new(hostId), saved.FlowId, CancellationToken.None)
            ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Json>();
            var candidate = AutomationAuthoredFlowCodec.Write(
                read.Original with
                {
                    Name = "Authorized repair",
                    Nodes =
                    [
                        .. read.Original.Nodes.Select(node =>
                            node.Id == send.Id.Value
                                ? node with
                                {
                                    ConfigurationJson = RepairedConfiguration,
                                }
                                : node
                        ),
                    ],
                }
            );
            var original = await ReadStoredAsync(database, saved.FlowId.Value);
            var access = services.GetRequiredService<HostModAccessService>();
            var page = ui.Context.Render<AutomationEditorPage>();
            page.WaitForAssertion(() =>
                page.FindComponent<AutomationFlowSource>().Instance.Text.ShouldNotBeEmpty()
            );
            return new(
                database,
                ui,
                page,
                access,
                http,
                hostId,
                otherHostId,
                saved.FlowId.Value,
                send.Id.Value,
                candidate,
                original
            );
        }

        private static async Task<StoredFlow> ReadStoredAsync(
            SqliteBlokeBotDbFactory database,
            Guid flowId
        )
        {
            await using var db = await database.CreateDbContextAsync();
            var flow = await db
                .AutomationFlows.AsNoTracking()
                .Include(value => value.Nodes)
                .Include(value => value.Edges)
                .SingleAsync(value => value.Id == flowId);
            return new(
                flow.Id,
                flow.Name,
                flow.IsEnabled,
                flow.UpdatedAtUtc,
                [
                    .. flow
                        .Nodes.OrderBy(node => node.Id)
                        .Select(node => new StoredNode(
                            node.Id,
                            node.ConfigurationJson,
                            node.InputBindingsJson,
                            node.PluginProvenanceJson
                        )),
                ],
                [.. flow.Edges.OrderBy(edge => edge.Id).Select(edge => edge.Id)]
            );
        }

        public async ValueTask DisposeAsync()
        {
            _ui.Context.Dispose();
            await _database.DisposeAsync();
        }
    }

    private sealed record StoredFlow(
        Guid Id,
        string Name,
        bool Enabled,
        DateTime UpdatedAtUtc,
        StoredNode[] Nodes,
        Guid[] EdgeIds
    );

    private sealed record StoredNode(
        Guid Id,
        string Configuration,
        string Bindings,
        string? Provenance
    );

    private sealed class FixtureAppTokenSource : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("fixture-app-token");
    }

    private sealed class RejectingChatSender : IPublicChatMessageSender
    {
        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PublicChatSendOutcome>(new PublicChatSendOutcome.Rejected());
    }

    private sealed class ControlledHttpClientFactory : IHttpClientFactory
    {
        private PendingAuthority? _pending;
        internal int RequestCount { get; private set; }

        internal PendingAuthority PauseNextRequest() => _pending = new();

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(ControlledHttpClientFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.RequestCount++;
                if (owner._pending is { } pending)
                {
                    owner._pending = null;
                    pending.Entered.SetResult();
                    await pending.Release.Task.WaitAsync(cancellationToken);
                }
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"data":[{"broadcaster_login":"streamer"}],"pagination":{}}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }
        }
    }

    private sealed record PendingAuthority
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
