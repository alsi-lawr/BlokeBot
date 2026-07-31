using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class OverlayCueServiceTests
{
    [Test]
    public async Task AssetLifecycle_ValidatesSignatureRetainsDataAndPreventsReferencedDelete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(fixture.HostId);
        await using var content = new MemoryStream(Mp4Bytes());

        var uploaded = (
            await fixture.Cues.UploadAssetAsync(
                session,
                "Celebration",
                "video/mp4",
                content,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        File.Exists(fixture.ContentPath(uploaded.Id)).ShouldBeTrue();
        fixture
            .ContentPath(uploaded.Id)
            .ShouldContain(Path.Combine("overlay-media", fixture.HostId.ToString()));
        fixture.ContentPath(uploaded.Id).ShouldNotContain("wwwroot");
        (await fixture.Cues.ListAssetsAsync(Session(fixture.OtherHostId), CancellationToken.None))
            .ShouldBeOfType<OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded>()
            .Value.ShouldBeEmpty();

        var cueJson =
            """{"schemaVersion":1,"layers":[{"type":"uploadedMedia","assetId":"ASSET_ID","mediaKind":"video","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":0,"volume":1,"fit":"contain","rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}]}""".Replace(
                "ASSET_ID",
                uploaded.Id.ToString("D"),
                StringComparison.Ordinal
            );
        (
            await fixture.Cues.SaveCueAsync(
                session,
                new(
                    null,
                    new(0),
                    "Celebration",
                    true,
                    1000,
                    OverlayCueQueuePolicy.Enqueue,
                    cueJson
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueResult<OverlayCueView>.Succeeded>();
        (
            await fixture.Cues.DeleteAssetAsync(
                session,
                uploaded.Id,
                uploaded.ContentRevision,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<Guid>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.InUse>();

        await fixture.SetFeaturesAsync(HostFeatureFlags.None);
        await using var suppressed = new MemoryStream(Mp4Bytes());
        (
            await fixture.Cues.UploadAssetAsync(
                session,
                "Suppressed",
                "video/mp4",
                suppressed,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.ParentDisabled>();
        await fixture.SetFeaturesAsync(HostFeatureFlags.Overlays);
        (await fixture.Cues.ListAssetsAsync(session, CancellationToken.None))
            .ShouldBeOfType<OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded>()
            .Value.ShouldHaveSingleItem()
            .Id.ShouldBe(uploaded.Id);
    }

    [Test]
    public async Task Upload_TruncatedOversizedAndQuotaRacesLeaveOnlyCommittedAssets()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = Session(fixture.HostId);
        await using (var truncated = new MemoryStream([0, 0, 0, 12, (byte)'f', (byte)'t']))
        {
            (
                await fixture.Cues.UploadAssetAsync(
                    session,
                    "Truncated",
                    "video/mp4",
                    truncated,
                    CancellationToken.None
                )
            ).ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>();
        }
        await using (var oversized = new MemoryStream(new byte[1025]))
        {
            (
                await fixture.Cues.UploadAssetAsync(
                    session,
                    "Oversized",
                    "video/mp4",
                    oversized,
                    CancellationToken.None
                )
            ).ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>();
        }

        var uploads = Enumerable
            .Range(0, 3)
            .Select(async index =>
            {
                await using var stream = new MemoryStream(Mp4Bytes(1024));
                return await fixture.Cues.UploadAssetAsync(
                    session,
                    $"Asset {index}",
                    "video/mp4",
                    stream,
                    CancellationToken.None
                );
            })
            .ToArray();
        var outcomes = await Task.WhenAll(uploads);

        outcomes
            .Count(value => value is OverlayCueResult<OverlayMediaAssetView>.Succeeded)
            .ShouldBe(2);
        outcomes
            .Count(value => value is OverlayCueResult<OverlayMediaAssetView>.Rejected)
            .ShouldBe(1);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.OverlayMediaAssets.CountAsync()).ShouldBe(2);
        Directory
            .EnumerateFiles(fixture.MediaRoot, "*", SearchOption.AllDirectories)
            .Count()
            .ShouldBe(2);
    }

    [Test]
    [Arguments("application/octet-stream")]
    [Arguments("audio/mpeg")]
    public async Task Upload_RejectsMismatchedMimeWithoutPublishingMetadata(string claimedMime)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var content = new MemoryStream(Mp4Bytes());

        (
            await fixture.Cues.UploadAssetAsync(
                Session(fixture.HostId),
                "Bad",
                claimedMime,
                content,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.Invalid>();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.OverlayMediaAssets.CountAsync()).ShouldBe(0);
        Directory
            .EnumerateFiles(fixture.MediaRoot, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Test]
    public async Task DeleteAsset_StorageFailureRetainsMetadataBytesAndQuota()
    {
        var deletion = new ControlledMediaFileDeletion();
        await using var fixture = await Fixture.CreateAsync(
            maximumHostStorageBytes: 1024,
            deletion
        );
        var session = Session(fixture.HostId);
        await using var original = new MemoryStream(Mp4Bytes(1024));
        var uploaded = (
            await fixture.Cues.UploadAssetAsync(
                session,
                "Original",
                "video/mp4",
                original,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        var originalPath = fixture.ContentPath(uploaded.Id);
        deletion.StorageKeysUnavailable = true;

        (
            await fixture.Cues.DeleteAssetAsync(
                session,
                uploaded.Id,
                uploaded.ContentRevision,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<Guid>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.StorageUnavailable>();

        File.Exists(originalPath).ShouldBeTrue();
        (await fixture.Cues.ListAssetsAsync(session, CancellationToken.None))
            .ShouldBeOfType<OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded>()
            .Value.ShouldHaveSingleItem()
            .ShouldBe(uploaded);
        await using var additional = new MemoryStream(Mp4Bytes());
        (
            await fixture.Cues.UploadAssetAsync(
                session,
                "Additional",
                "video/mp4",
                additional,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.Invalid>();
    }

    [Test]
    public async Task ReplaceAsset_StorageFailureRetainsOldRevisionBytesAndQuota()
    {
        var deletion = new ControlledMediaFileDeletion();
        await using var fixture = await Fixture.CreateAsync(
            maximumHostStorageBytes: 1024,
            deletion
        );
        var session = Session(fixture.HostId);
        await using var original = new MemoryStream(Mp4Bytes(1024));
        var uploaded = (
            await fixture.Cues.UploadAssetAsync(
                session,
                "Original",
                "video/mp4",
                original,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        var originalPath = fixture.ContentPath(uploaded.Id);
        deletion.StorageKeysUnavailable = true;
        await using var replacement = new MemoryStream(Mp4Bytes());

        (
            await fixture.Cues.ReplaceAssetAsync(
                session,
                new(uploaded.Id, uploaded.ContentRevision, "video/mp4", replacement),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.StorageUnavailable>();

        File.Exists(originalPath).ShouldBeTrue();
        (await fixture.Cues.ListAssetsAsync(session, CancellationToken.None))
            .ShouldBeOfType<OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded>()
            .Value.ShouldHaveSingleItem()
            .ShouldBe(uploaded);
        var survivingFiles = Directory
            .EnumerateFiles(fixture.MediaRoot, "*", SearchOption.AllDirectories)
            .ToArray();
        survivingFiles.Length.ShouldBe(2);
        survivingFiles.ShouldContain(originalPath);
        survivingFiles.Sum(path => new FileInfo(path).Length).ShouldBe(1036);
        await using var additional = new MemoryStream(Mp4Bytes());
        (
            await fixture.Cues.UploadAssetAsync(
                session,
                "Additional",
                "video/mp4",
                additional,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.Invalid>();
    }

    [Test]
    public async Task Upload_StaleTemporaryFileConsumesQuotaButCurrentUploadDoesNot()
    {
        var deletion = new ControlledMediaFileDeletion { TemporaryFilesUnavailable = true };
        await using var fixture = await Fixture.CreateAsync(
            maximumHostStorageBytes: 1024,
            deletion
        );
        var session = Session(fixture.HostId);
        await using (var rejected = new MemoryStream(Mp4Bytes(1024)))
        {
            (
                await fixture.Cues.UploadAssetAsync(
                    session,
                    "Rejected",
                    "audio/mpeg",
                    rejected,
                    CancellationToken.None
                )
            ).ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>();
        }
        var staleUpload = Directory
            .EnumerateFiles(fixture.MediaRoot, ".upload-*", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
        new FileInfo(staleUpload).Length.ShouldBe(1024);
        deletion.TemporaryFilesUnavailable = false;
        await using var additional = new MemoryStream(Mp4Bytes());

        (
            await fixture.Cues.UploadAssetAsync(
                session,
                "Additional",
                "video/mp4",
                additional,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.Invalid>()
            .Message.ShouldContain("quota");

        File.Exists(staleUpload).ShouldBeTrue();
        Directory
            .EnumerateFiles(fixture.MediaRoot, "*", SearchOption.AllDirectories)
            .ShouldHaveSingleItem()
            .ShouldBe(staleUpload);
    }

    [Test]
    public async Task Admission_IsHostBoundDeterministicAndCancelsOnParentDisable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (target, cue) = await fixture.SeedPlaybackAsync();
        await fixture.Playback.StartAsync(CancellationToken.None);

        var disconnected = await fixture.Playback.AdmitAsync(
            Request(fixture.HostId, target, cue, OverlayCueQueuePolicy.Enqueue),
            CancellationToken.None
        );
        disconnected.ShouldBeOfType<OverlayCueAdmissionOutcome.Disconnected>();
        fixture.Presence.Connected = true;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
        _ = await fixture.Transport.ReadStartedAsync();
        fixture.Transport.Started.Count.ShouldBe(1);

        (
            await fixture.Playback.AdmitAsync(
                Request(fixture.HostId, target, cue, OverlayCueQueuePolicy.Enqueue),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Queued>();
        (
            await fixture.Playback.AdmitAsync(
                Request(fixture.HostId, target, cue, OverlayCueQueuePolicy.Ignore),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.QueueRejected>();
        (
            await fixture.Playback.AdmitAsync(
                Request(fixture.HostId, target, cue, OverlayCueQueuePolicy.Concurrent),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Running>();
        _ = await fixture.Transport.ReadStartedAsync();
        (
            await fixture.Playback.AdmitAsync(
                Request(fixture.OtherHostId, target, cue, OverlayCueQueuePolicy.Enqueue),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Missing>();

        await fixture.SetFeaturesAsync(HostFeatureFlags.None);
        await fixture.Events.PublishAsync(AppEventKind.OverlaysChanged, CancellationToken.None);
        _ = await fixture.Transport.ReadStoppedAsync();
        _ = await fixture.Transport.ReadStoppedAsync();
        fixture.Transport.Stopped.Count.ShouldBe(2);
    }

    [Test]
    public async Task CueReferenceResolution_IsHostBoundAndDistinguishesMissingAndDisabledParts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (target, cue) = await fixture.SeedPlaybackAsync();

        (
            await fixture.Playback.ResolveReferencesAsync(
                new(fixture.HostId, target, cue),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueReferenceOutcome.Available>();
        (
            await fixture.Playback.ResolveReferencesAsync(
                new(fixture.OtherHostId, target, cue),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueReferenceOutcome.Missing>()
            .Part.ShouldBe(OverlayCueReferencePart.Target);

        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var storedCue = await db.OverlayCues.SingleAsync(value => value.PublicId == cue);
            storedCue.IsEnabled = false;
            await db.SaveChangesAsync();
        }
        (
            await fixture.Playback.ResolveReferencesAsync(
                new(fixture.HostId, target, cue),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueReferenceOutcome.Disabled>()
            .Part.ShouldBe(OverlayCueReferencePart.Cue);

        await fixture.SetFeaturesAsync(HostFeatureFlags.None);
        (
            await fixture.Playback.ResolveReferencesAsync(
                new(fixture.HostId, target, cue),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayCueReferenceOutcome.Disabled>()
            .Part.ShouldBe(OverlayCueReferencePart.Parent);
    }

    [Test]
    public async Task PlaybackTimeout_CompletesFailedRunAndAdvancesQueuedCue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var (target, cue) = await fixture.SeedPlaybackAsync(durationMilliseconds: 100);
        fixture.Presence.Connected = true;
        await fixture.Playback.StartAsync(CancellationToken.None);

        (
            await fixture.Playback.AdmitAsync(
                Request(fixture.HostId, target, cue, OverlayCueQueuePolicy.Enqueue),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Running>();
        var firstRun = await fixture.Transport.ReadStartedAsync();
        (
            await fixture.Playback.AdmitAsync(
                Request(fixture.HostId, target, cue, OverlayCueQueuePolicy.Enqueue),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Queued>();

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1250));
        (await fixture.Transport.ReadStoppedAsync()).ShouldBe(firstRun);
        (await fixture.Transport.ReadStartedAsync()).ShouldNotBe(firstRun);
        fixture.Transport.Stopped.Count.ShouldBe(1);
        fixture.Transport.Started.Count.ShouldBe(2);
    }

    private static OverlayCueAdmissionRequest Request(
        int hostId,
        Guid target,
        Guid cue,
        OverlayCueQueuePolicy policy
    )
    {
        return new(
            hostId,
            target,
            cue,
            policy,
            OverlayCueAdmissionOrigin.Command,
            new("viewer", "Viewer")
        );
    }

    private static byte[] Mp4Bytes(int length = 12)
    {
        var bytes = new byte[length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)length));
        "ftypisom"u8.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    private static AuthenticatedSession Session(int hostId)
    {
        var host = new BotHostChoice(hostId, "host", "Host", AuthRole.Streamer);
        return new()
        {
            IsAuthenticated = true,
            UserId = "owner-id",
            Login = "owner",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteBlokeBotDbFactory database,
            int hostId,
            int otherHostId,
            string mediaRoot,
            OverlayCueService cues,
            OverlayCuePlaybackService playback,
            FakePresence presence,
            FakeTransport transport,
            EventBus<AppEventKind> events,
            ManualTimeProvider clock
        )
        {
            Database = database;
            HostId = hostId;
            OtherHostId = otherHostId;
            MediaRoot = mediaRoot;
            Cues = cues;
            Playback = playback;
            Presence = presence;
            Transport = transport;
            Events = events;
            Clock = clock;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal int HostId { get; }
        internal int OtherHostId { get; }
        internal string MediaRoot { get; }
        internal OverlayCueService Cues { get; }
        internal OverlayCuePlaybackService Playback { get; }
        internal FakePresence Presence { get; }
        internal FakeTransport Transport { get; }
        internal EventBus<AppEventKind> Events { get; }
        internal ManualTimeProvider Clock { get; }

        internal static async Task<Fixture> CreateAsync(
            long maximumHostStorageBytes = 2048,
            IOverlayMediaFileDeletion? fileDeletion = null
        )
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            int hostId;
            int otherHostId;
            await using (var db = await database.CreateDbContextAsync())
            {
                var host = Host("host");
                var other = Host("other");
                db.Hosts.AddRange(host, other);
                await db.SaveChangesAsync();
                hostId = host.Id;
                otherHostId = other.Id;
            }
            var root = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-overlay-cue-tests-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(root);
            var options = Options.Create(
                new BlokeBotOptions
                {
                    DatabasePath = Path.Combine(root, "state.db"),
                    Overlays = new()
                    {
                        Media = new()
                        {
                            MaximumUploadBytes = 1024,
                            MaximumHostStorageBytes = maximumHostStorageBytes,
                            DisconnectedQueueExpirySeconds = 5,
                        },
                    },
                }
            );
            var clock = new ManualTimeProvider(
                new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)
            );
            var dns = new OverlayRemoteUrlPolicy(new PublicDnsResolver(), options);
            var authority = new OverlayManagementAuthority(
                database,
                new GrantedModeratorAuthority()
            );
            var events = TestEventBus.Create<AppEventKind>();
            var cues = new OverlayCueService(
                database,
                authority,
                dns,
                options,
                events,
                clock,
                fileDeletion ?? new SystemOverlayMediaFileDeletion()
            );
            var presence = new FakePresence();
            var transport = new FakeTransport();
            var playback = new OverlayCuePlaybackService(
                database,
                dns,
                presence,
                transport,
                options,
                events,
                clock,
                NullLogger<OverlayCuePlaybackService>.Instance
            );
            return new(
                database,
                hostId,
                otherHostId,
                root,
                cues,
                playback,
                presence,
                transport,
                events,
                clock
            );
        }

        internal async Task<(Guid Target, Guid Cue)> SeedPlaybackAsync(
            int durationMilliseconds = 1000
        )
        {
            await using var db = await Database.CreateDbContextAsync();
            var target = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = HostId,
                Name = "Cue player",
                Type = OverlayType.CuePlayer,
                IsEnabled = true,
                ConfigurationJson = """{"schemaVersion":1}""",
                AccessKeyDigest = Enumerable.Repeat((byte)7, 32).ToArray(),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            var cue = new OverlayCue
            {
                PublicId = Guid.NewGuid(),
                HostId = HostId,
                Name = "Remote",
                IsEnabled = true,
                DurationMilliseconds = durationMilliseconds,
                QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                ConfigurationJson =
                    """{"schemaVersion":1,"layers":[{"type":"externalWeb","url":"https://example.test/","startOffsetMilliseconds":0,"durationMilliseconds":DURATION,"zIndex":0,"rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}]}""".Replace(
                        "DURATION",
                        durationMilliseconds.ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        ),
                        StringComparison.Ordinal
                    ),
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            db.AddRange(target, cue);
            await db.SaveChangesAsync();
            return (target.PublicId, cue.PublicId);
        }

        internal async Task SetFeaturesAsync(HostFeatureFlags features)
        {
            await using var db = await Database.CreateDbContextAsync();
            await db
                .Hosts.Where(value => value.Id == HostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(value => value.EnabledFeatures, features)
                );
        }

        internal string ContentPath(Guid assetId)
        {
            using var db = Database.CreateDbContext();
            var asset = db.OverlayMediaAssets.Single(value => value.PublicId == assetId);
            return Path.Combine(MediaRoot, "overlay-media", HostId.ToString(), asset.StorageKey);
        }

        public async ValueTask DisposeAsync()
        {
            await Playback.DisposeAsync();
            await Database.DisposeAsync();
            Directory.Delete(MediaRoot, recursive: true);
        }

        private static BotHost Host(string login)
        {
            return new()
            {
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                EnabledFeatures = HostFeatureFlags.Overlays,
                CreatedAtUtc = DateTime.UtcNow,
            };
        }
    }

    private sealed class GrantedModeratorAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        )
        {
            return Task.FromResult<ModeratorAuthorityOutcome>(
                new ModeratorAuthorityOutcome.Granted()
            );
        }
    }

    private sealed class PublicDnsResolver : IOverlayDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("203.0.113.10")]);
        }
    }

    private sealed class FakePresence : IOverlayLivePresence
    {
        internal bool Connected { get; set; }

        public OverlayConnectionPresence Read(int hostId, Guid overlayId)
        {
            return new() { ActiveConnectionCount = Connected ? 1 : 0 };
        }
    }

    private sealed class FakeTransport : IOverlayCueTransport
    {
        private readonly Channel<Guid> _started = Channel.CreateUnbounded<Guid>();
        private readonly Channel<Guid> _stopped = Channel.CreateUnbounded<Guid>();

        internal ConcurrentQueue<Guid> Started { get; } = [];
        internal ConcurrentQueue<Guid> Stopped { get; } = [];

        public void Start(ResolvedOverlayInstance target, OverlayCuePlaybackPlan plan)
        {
            Started.Enqueue(plan.RunId);
            _started.Writer.TryWrite(plan.RunId).ShouldBeTrue();
        }

        public void Stop(ResolvedOverlayInstance target, Guid runId)
        {
            Stopped.Enqueue(runId);
            _stopped.Writer.TryWrite(runId).ShouldBeTrue();
        }

        internal ValueTask<Guid> ReadStartedAsync()
        {
            return _started.Reader.ReadAsync();
        }

        internal ValueTask<Guid> ReadStoppedAsync()
        {
            return _stopped.Reader.ReadAsync();
        }
    }

    private sealed class ControlledMediaFileDeletion : IOverlayMediaFileDeletion
    {
        internal bool StorageKeysUnavailable { get; set; }
        internal bool TemporaryFilesUnavailable { get; set; }

        public OverlayMediaFileDeletionOutcome Delete(string path)
        {
            var fileName = Path.GetFileName(path);
            if (
                (
                    StorageKeysUnavailable
                    && fileName.Length == 32
                    && fileName.All(character =>
                        character is >= '0' and <= '9' or >= 'a' and <= 'f'
                    )
                )
                || (
                    TemporaryFilesUnavailable
                    && fileName.StartsWith(".upload-", StringComparison.Ordinal)
                )
            )
            {
                return new OverlayMediaFileDeletionOutcome.Unavailable();
            }
            File.Delete(path);
            return new OverlayMediaFileDeletionOutcome.Deleted();
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly HashSet<ManualTimer> _timers = [];
        private DateTimeOffset _now = initialNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override long GetTimestamp()
        {
            return GetUtcNow().UtcTicks;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _now = _now.Add(duration);
                due = _timers.Where(timer => timer.IsDue(_now)).ToArray();
            }
            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return false;
                    }
                    _period = period;
                    _dueAt =
                        dueTime == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner._now.Add(dueTime);
                    owner._timers.Add(this);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    _disposed = true;
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal bool IsDue(DateTimeOffset now)
            {
                return !_disposed && _dueAt <= now;
            }

            internal void Fire()
            {
                lock (owner._gate)
                {
                    if (!IsDue(owner._now))
                    {
                        return;
                    }
                    _dueAt =
                        _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan
                            ? owner._now.Add(_period)
                            : DateTimeOffset.MaxValue;
                }
                callback(state);
            }
        }
    }
}
