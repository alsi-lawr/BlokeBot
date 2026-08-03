using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayInstanceServiceTests
{
    [Test]
    public void ConfigurationParser_AcceptsOnlyBoundedVersionedTypedJson()
    {
        _ = OverlayConfiguration
            .Parse(OverlayType.Empty, """{"schemaVersion":1}""")
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
            .Value.ShouldBeOfType<OverlayConfiguration.EmptyV1>();
        _ = OverlayConfiguration
            .Parse(OverlayType.Empty, "not-json")
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        _ = OverlayConfiguration
            .Parse(OverlayType.Empty, """{"schemaVersion":2}""")
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        _ = OverlayConfiguration
            .Parse(OverlayType.Empty, """{"schemaVersion":1,"unbounded":"future-shape"}""")
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        _ = OverlayConfiguration
            .Parse(OverlayType.Empty, new string('x', 4097))
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        _ = OverlayConfiguration
            .Parse((OverlayType)999, """{"schemaVersion":1}""")
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        var guessing = OverlayConfiguration
            .Parse(
                OverlayType.Guessing,
                """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
            .Value.ShouldBeOfType<OverlayConfiguration.GuessingV1>();
        guessing.ShowGuessCount.ShouldBeTrue();
        guessing.ResultDurationSeconds.ShouldBe(8);
        _ = OverlayConfiguration
            .Parse(
                OverlayType.Guessing,
                """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":31}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        _ = OverlayConfiguration
            .Parse(
                OverlayType.Guessing,
                """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8,"extra":1}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
    }

    [Test]
    public void CryptographicGenerator_ProducesCanonicalIndependent256BitKeys()
    {
        var generator = new CryptographicOverlayAccessKeyGenerator();

        var keys = Enumerable.Range(0, 256).Select(_ => generator.Generate()).ToArray();

        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(keys.Length);
        foreach (var key in keys)
        {
            key.Length.ShouldBe(43);
            key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                .ShouldBeTrue();
        }
    }

    [Test]
    public async Task StreamerCreate_PersistsOnlyDigestAndRedactedAudit_ThenListsAndResolves()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(AuthRole.Streamer, fixture.HostId);

        var result = await fixture.Service.CreateAsync(
            session,
            Create("Starting soon"),
            CancellationToken.None
        );

        var created = result.SucceededValue();
        created.Instance.Name.ShouldBe("Starting soon");
        created.Instance.Type.ShouldBe(OverlayType.Empty);
        created.Instance.IsEnabled.ShouldBeTrue();
        created.Instance.Revision.ShouldBe(new OverlayRevision(1));
        created.PrivateAccess.AccessKey.Length.ShouldBe(43);
        created.PrivateAccess.RelativeUrl.ShouldBe($"/overlay/{created.PrivateAccess.AccessKey}");
        created.PrivateAccess.ToString().ShouldBe("[REDACTED OVERLAY ACCESS]");
        JsonSerializer
            .Serialize(created.PrivateAccess)
            .ShouldNotContain(created.PrivateAccess.AccessKey);

        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var persisted = await db.OverlayInstances.SingleAsync();
            persisted.Id.ShouldBeGreaterThan(0);
            persisted.PublicId.ShouldBe(created.Instance.Id);
            persisted.AccessKeyDigest.Length.ShouldBe(32);
            Convert
                .ToHexString(persisted.AccessKeyDigest)
                .ShouldNotContain(created.PrivateAccess.AccessKey);
            persisted.ConfigurationJson.ShouldBe("""{"schemaVersion":1}""");
            var audit = await db.OverlayInstanceEvents.SingleAsync();
            audit.Kind.ShouldBe(OverlayInstanceEventKind.Created);
            audit.OverlayPublicId.ShouldBe(created.Instance.Id);
            audit.ActorUserId.ShouldBe("actor-id");
            JsonSerializer.Serialize(audit).ShouldNotContain(created.PrivateAccess.AccessKey);
        }

        fixture.Logger.Messages.ShouldNotContain(message =>
            message.Contains(created.PrivateAccess.AccessKey, StringComparison.Ordinal)
        );
        var listed = (
            await fixture.Service.ListAsync(session, CancellationToken.None)
        ).SucceededValue();
        listed.ShouldHaveSingleItem().ShouldBe(created.Instance);
        var fetched = (
            await fixture.Service.GetAsync(session, created.Instance.Id, CancellationToken.None)
        ).SucceededValue();
        fetched.ShouldBe(created.Instance);
        var resolved = await fixture.Resolver.ResolveAsync(
            created.PrivateAccess.AccessKey,
            CancellationToken.None
        );
        var authoritative = resolved.ShouldBeOfType<OverlayResolutionResult.Resolved>().Instance;
        authoritative.HostId.ShouldBe(fixture.HostId);
        authoritative.OverlayId.ShouldBe(created.Instance.Id);
    }

    [Test]
    public async Task ModeratorAuthorityAndSelectedHost_IsolateAllManagementOperations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = Session(AuthRole.Streamer, fixture.HostId);
        var created = (
            await fixture.Service.CreateAsync(
                owner,
                Create("Owner overlay"),
                CancellationToken.None
            )
        ).SucceededValue();

        fixture.Authority.Outcome = new ModeratorAuthorityOutcome.Granted();
        var moderator = Session(AuthRole.Moderator, fixture.HostId);
        (
            await fixture.Service.RenameAsync(
                moderator,
                new(created.Instance.Id, created.Instance.Revision, "Moderator rename"),
                CancellationToken.None
            )
        )
            .SucceededValue()
            .Name.ShouldBe("Moderator rename");
        fixture.Authority.RequestedHostIds.ShouldBe([fixture.HostId]);

        fixture.Authority.Outcome = new ModeratorAuthorityOutcome.Revoked();
        _ = (
            await fixture.Service.RotateKeyAsync(
                moderator,
                new(created.Instance.Id, new OverlayRevision(2)),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceKeyRotation>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.Unauthorized>();

        _ = (
            await fixture.Service.DeleteAsync(
                Session(AuthRole.Bot, fixture.HostId, isBot: true),
                new(created.Instance.Id, new OverlayRevision(2)),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<Guid>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.Unauthorized>();
        _ = (
            await fixture.Service.CreateAsync(
                AuthenticatedSession.Anonymous,
                Create("Denied create"),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceCreation>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.Unauthorized>();

        _ = (
            await fixture.Service.RenameAsync(
                Session(AuthRole.Streamer, fixture.OtherHostId),
                new(created.Instance.Id, new OverlayRevision(2), "Cross-host rename"),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.NotFound>();
        _ = (
            await fixture.Service.GetAsync(
                Session(AuthRole.Streamer, fixture.OtherHostId),
                created.Instance.Id,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.NotFound>();
    }

    [Test]
    public async Task RenameAndConfigure_RequireCurrentRevisionAndPreserveTypedConfiguration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(AuthRole.Streamer, fixture.HostId);
        var created = (
            await fixture.Service.CreateAsync(session, Create("Original"), CancellationToken.None)
        ).SucceededValue();

        var renamed = (
            await fixture.Service.RenameAsync(
                session,
                new(created.Instance.Id, created.Instance.Revision, "Renamed"),
                CancellationToken.None
            )
        ).SucceededValue();
        renamed.Name.ShouldBe("Renamed");
        renamed.Revision.ShouldBe(new OverlayRevision(2));
        _ = (
            await fixture.Service.RenameAsync(
                session,
                new(created.Instance.Id, created.Instance.Revision, "Stale"),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.Conflict>();

        var configured = (
            await fixture.Service.ConfigureAsync(
                session,
                new(created.Instance.Id, renamed.Revision, new OverlayConfiguration.EmptyV1()),
                CancellationToken.None
            )
        ).SucceededValue();
        _ = configured.Configuration.ShouldBeOfType<OverlayConfiguration.EmptyV1>();
        configured.Revision.ShouldBe(new OverlayRevision(3));
    }

    [Test]
    public async Task RotateDisableEnableDelete_ImmediatelyRevokeAndRestoreOnlyCurrentKey()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(AuthRole.Streamer, fixture.HostId);
        var created = (
            await fixture.Service.CreateAsync(session, Create("Lifecycle"), CancellationToken.None)
        ).SucceededValue();
        var originalKey = created.PrivateAccess.AccessKey;

        var rotated = (
            await fixture.Service.RotateKeyAsync(
                session,
                new(created.Instance.Id, created.Instance.Revision),
                CancellationToken.None
            )
        ).SucceededValue();
        rotated.PrivateAccess.AccessKey.ShouldNotBe(originalKey);
        _ = (
            await fixture.Resolver.ResolveAsync(originalKey, CancellationToken.None)
        ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
        _ = (
            await fixture.Resolver.ResolveAsync(
                rotated.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();

        var disabled = (
            await fixture.Service.DisableAsync(
                session,
                new(rotated.Instance.Id, rotated.Instance.Revision),
                CancellationToken.None
            )
        ).SucceededValue();
        _ = (
            await fixture.Resolver.ResolveAsync(
                rotated.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
        var enabled = (
            await fixture.Service.EnableAsync(
                session,
                new(disabled.Id, disabled.Revision),
                CancellationToken.None
            )
        ).SucceededValue();
        _ = (
            await fixture.Resolver.ResolveAsync(
                rotated.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();

        (
            await fixture.Service.DeleteAsync(
                session,
                new(enabled.Id, enabled.Revision),
                CancellationToken.None
            )
        )
            .SucceededValue()
            .ShouldBe(enabled.Id);
        _ = (
            await fixture.Resolver.ResolveAsync(
                rotated.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
        _ = (await fixture.Service.GetAsync(session, enabled.Id, CancellationToken.None))
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.NotFound>();

        await using var db = await fixture.Database.CreateDbContextAsync();
        (
            await db
                .OverlayInstanceEvents.OrderBy(value => value.Id)
                .Select(value => value.Kind)
                .ToArrayAsync()
        ).ShouldBe([
            OverlayInstanceEventKind.Created,
            OverlayInstanceEventKind.KeyRotated,
            OverlayInstanceEventKind.Disabled,
            OverlayInstanceEventKind.Enabled,
            OverlayInstanceEventKind.Deleted,
        ]);
    }

    [Test]
    public async Task ConcurrentRotations_WithSameRevision_HaveOneWinnerAndNoStaleKey()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(AuthRole.Streamer, fixture.HostId);
        var created = (
            await fixture.Service.CreateAsync(session, Create("Concurrent"), CancellationToken.None)
        ).SucceededValue();
        var command = new RotateOverlayInstanceKeyCommand(
            created.Instance.Id,
            created.Instance.Revision
        );

        var outcomes = await Task.WhenAll(
            fixture.Service.RotateKeyAsync(session, command, CancellationToken.None),
            fixture.Service.RotateKeyAsync(session, command, CancellationToken.None)
        );

        var succeeded = outcomes
            .OfType<OverlayInstanceResult<OverlayInstanceKeyRotation>.Succeeded>()
            .ShouldHaveSingleItem()
            .Value;
        _ = outcomes
            .OfType<OverlayInstanceResult<OverlayInstanceKeyRotation>.Rejected>()
            .ShouldHaveSingleItem()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.Conflict>();
        _ = (
            await fixture.Resolver.ResolveAsync(
                created.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
        _ = (
            await fixture.Resolver.ResolveAsync(
                succeeded.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
    }

    [Test]
    public async Task ConcurrentDisableAndRotate_WithSameRevision_HaveOneAtomicOutcome()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(AuthRole.Streamer, fixture.HostId);
        var created = (
            await fixture.Service.CreateAsync(
                session,
                Create("Concurrent availability"),
                CancellationToken.None
            )
        ).SucceededValue();

        var disable = fixture.Service.DisableAsync(
            session,
            new(created.Instance.Id, created.Instance.Revision),
            CancellationToken.None
        );
        var rotate = fixture.Service.RotateKeyAsync(
            session,
            new(created.Instance.Id, created.Instance.Revision),
            CancellationToken.None
        );
        await Task.WhenAll(disable, rotate);

        var mutationSuccessCount =
            (disable.Result is OverlayInstanceResult<OverlayInstanceView>.Succeeded ? 1 : 0)
            + (
                rotate.Result is OverlayInstanceResult<OverlayInstanceKeyRotation>.Succeeded ? 1 : 0
            );
        mutationSuccessCount.ShouldBe(1);
        var current = (
            await fixture.Service.GetAsync(session, created.Instance.Id, CancellationToken.None)
        ).SucceededValue();
        current.Revision.ShouldBe(new OverlayRevision(2));
        if (rotate.Result is OverlayInstanceResult<OverlayInstanceKeyRotation>.Succeeded rotated)
        {
            _ = (
                await fixture.Resolver.ResolveAsync(
                    rotated.Value.PrivateAccess.AccessKey,
                    CancellationToken.None
                )
            ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
        }
        else
        {
            current.IsEnabled.ShouldBeFalse();
            _ = (
                await fixture.Resolver.ResolveAsync(
                    created.PrivateAccess.AccessKey,
                    CancellationToken.None
                )
            ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
        }
    }

    [Test]
    public async Task GuessingParents_BlockCreationMutationAndResolutionWhileRetainingSetup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(AuthRole.Streamer, fixture.HostId);
        var command = new CreateOverlayInstanceCommand(
            "Guessing",
            OverlayType.Guessing,
            new OverlayConfiguration.GuessingV1(true, 9)
        );
        var created = (
            await fixture.Service.CreateAsync(session, command, CancellationToken.None)
        ).SucceededValue();

        await fixture.SetFeaturesAsync(HostFeatureFlags.Overlays);

        _ = (
            await fixture.Resolver.ResolveAsync(
                created.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
        _ = (
            await fixture.Service.RenameAsync(
                session,
                new(created.Instance.Id, created.Instance.Revision, "Suppressed rename"),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.FeatureDisabled>();
        _ = (await fixture.Service.CreateAsync(session, command, CancellationToken.None))
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceCreation>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.FeatureDisabled>();

        await fixture.SetFeaturesAsync(HostFeatureFlags.Guessing);
        _ = (
            await fixture.Service.ConfigureAsync(
                session,
                new(
                    created.Instance.Id,
                    created.Instance.Revision,
                    new OverlayConfiguration.GuessingV1(false, 5)
                ),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayInstanceRejection.FeatureDisabled>();

        await fixture.SetFeaturesAsync(HostFeatureFlags.Overlays | HostFeatureFlags.Guessing);
        var restored = (
            await fixture.Service.GetAsync(session, created.Instance.Id, CancellationToken.None)
        ).SucceededValue();
        restored.Name.ShouldBe("Guessing");
        restored.Revision.ShouldBe(created.Instance.Revision);
        restored.Configuration.ShouldBe(new OverlayConfiguration.GuessingV1(true, 9));
        _ = (
            await fixture.Resolver.ResolveAsync(
                created.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.OverlayInstances.CountAsync()).ShouldBe(1);
        (await db.OverlayInstanceEvents.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task GiveawayParents_BlockCreationMutationAndResolutionWhileRetainingSetup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(AuthRole.Streamer, fixture.HostId);
        var configuration = new OverlayConfiguration.GiveawayV1(
            "Community giveaway",
            true,
            true,
            true
        );
        var command = new CreateOverlayInstanceCommand(
            "Giveaway",
            OverlayType.Giveaway,
            configuration
        );
        var created = (
            await fixture.Service.CreateAsync(session, command, CancellationToken.None)
        ).SucceededValue();

        foreach (var incomplete in new[] { HostFeatureFlags.Overlays, HostFeatureFlags.Points })
        {
            await fixture.SetFeaturesAsync(incomplete);
            _ = (
                await fixture.Resolver.ResolveAsync(
                    created.PrivateAccess.AccessKey,
                    CancellationToken.None
                )
            ).ShouldBeOfType<OverlayResolutionResult.NotFound>();
            _ = (
                await fixture.Service.ConfigureAsync(
                    session,
                    new(created.Instance.Id, created.Instance.Revision, configuration),
                    CancellationToken.None
                )
            )
                .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Rejected>()
                .Reason.ShouldBeOfType<OverlayInstanceRejection.FeatureDisabled>();
            _ = (await fixture.Service.CreateAsync(session, command, CancellationToken.None))
                .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceCreation>.Rejected>()
                .Reason.ShouldBeOfType<OverlayInstanceRejection.FeatureDisabled>();
        }

        await fixture.SetFeaturesAsync(HostFeatureFlags.Overlays | HostFeatureFlags.Points);
        var restored = (
            await fixture.Service.GetAsync(session, created.Instance.Id, CancellationToken.None)
        ).SucceededValue();
        restored.Name.ShouldBe("Giveaway");
        restored.Revision.ShouldBe(created.Instance.Revision);
        restored.Configuration.ShouldBe(configuration);
        _ = (
            await fixture.Resolver.ResolveAsync(
                created.PrivateAccess.AccessKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.OverlayInstances.CountAsync()).ShouldBe(1);
        (await db.OverlayInstanceEvents.CountAsync()).ShouldBe(1);
    }

    private static CreateOverlayInstanceCommand Create(string name) =>
        new(name, OverlayType.Empty, new OverlayConfiguration.EmptyV1());

    private static AuthenticatedSession Session(AuthRole role, int hostId, bool isBot = false)
    {
        var host = new BotHostChoice(hostId, $"host-{hostId}", $"Host {hostId}", role);
        return new()
        {
            IsAuthenticated = true,
            UserId = "actor-id",
            Login = "actor",
            IsBotAccount = isBot,
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteBlokeBotDbFactory database,
            int hostId,
            int otherHostId,
            FakeModeratorAuthority authority,
            RecordingLogger logger,
            OverlayInstanceService service
        )
        {
            Database = database;
            HostId = hostId;
            OtherHostId = otherHostId;
            Authority = authority;
            Logger = logger;
            Service = service;
            Resolver = new OverlayInstanceResolver(database);
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal int HostId { get; }
        internal int OtherHostId { get; }
        internal FakeModeratorAuthority Authority { get; }
        internal RecordingLogger Logger { get; }
        internal OverlayInstanceService Service { get; }
        internal OverlayInstanceResolver Resolver { get; }

        internal async Task SetFeaturesAsync(HostFeatureFlags features)
        {
            await using var db = await Database.CreateDbContextAsync();
            _ = await db
                .Hosts.Where(host => host.Id == HostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(host => host.EnabledFeatures, features)
                );
        }

        internal static async Task<Fixture> CreateAsync()
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            int hostId;
            int otherHostId;
            await using (var db = await database.CreateDbContextAsync())
            {
                var host = Host("host");
                var other = Host("other");
                db.Hosts.AddRange(host, other);
                _ = await db.SaveChangesAsync();
                hostId = host.Id;
                otherHostId = other.Id;
            }

            var authority = new FakeModeratorAuthority();
            var logger = new RecordingLogger();
            var clock = new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
            );
            var service = new OverlayInstanceService(
                database,
                authority,
                new CryptographicOverlayAccessKeyGenerator(),
                TestEventBus.Create<AppEventKind>(),
                clock,
                logger
            );
            return new Fixture(database, hostId, otherHostId, authority, logger, service);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();

        private static BotHost Host(string login) =>
            new()
            {
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = DateTime.UtcNow,
            };
    }

    private sealed class FakeModeratorAuthority : IModeratorAuthorityService
    {
        internal ModeratorAuthorityOutcome Outcome { get; set; } =
            new ModeratorAuthorityOutcome.Granted();
        internal List<int> RequestedHostIds { get; } = [];

        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        )
        {
            RequestedHostIds.Add(requestedHostId);
            return Task.FromResult(Outcome);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger : ILogger<OverlayInstanceService>
    {
        internal List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }
}

internal static class OverlayInstanceTestResultExtensions
{
    internal static T SucceededValue<T>(this OverlayInstanceResult<T> result) =>
        result.ShouldBeOfType<OverlayInstanceResult<T>.Succeeded>().Value;
}
