using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class ViewerPassportPrivacySafetyTests
{
    private static readonly DateTime _now = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task StableId_IgnoresMismatchedAliasAndErasesOnlyItsUniqueRememberedHistory()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        long ownerPassportId;
        int ownerLedgerId;
        int otherLedgerId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = Host("channel");
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            var owner = await AddPassportAsync(
                seed,
                hostId,
                "owner-id",
                "owner_new",
                ["owner_old", "owner_new"]
            );
            ownerPassportId = owner.Id;
            _ = await AddPassportAsync(
                seed,
                hostId,
                "other-id",
                "other_new",
                ["other_old", "other_new"]
            );
            seed.PointBalances.AddRange(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "owner_old",
                    Amount = "10",
                    UpdatedAtUtc = _now,
                },
                new PointBalance
                {
                    HostId = hostId,
                    Login = "other_old",
                    Amount = "20",
                    UpdatedAtUtc = _now,
                }
            );
            var ownerLedger = new PointLedgerEntry
            {
                HostId = hostId,
                CreatedAtUtc = _now,
                Kind = PointLedgerKind.Add,
                Login = "system",
                ActorLogin = "owner_old",
                CounterpartyLogin = "owner_new",
                Delta = "10",
                BalanceAfter = "10",
                Note = "private owner_old adjustment",
            };
            var otherLedger = new PointLedgerEntry
            {
                HostId = hostId,
                CreatedAtUtc = _now,
                Kind = PointLedgerKind.Add,
                Login = "system",
                ActorLogin = "other_old",
                CounterpartyLogin = "other_new",
                Delta = "20",
                BalanceAfter = "20",
                Note = "private other_old adjustment",
            };
            seed.PointLedgerEntries.AddRange(ownerLedger, otherLedger);
            _ = seed.ViewerPassportAttendanceDays.Add(
                new()
                {
                    HostId = hostId,
                    PassportId = owner.Id,
                    DateUtc = new DateOnly(2026, 8, 11),
                    FirstSeenAtUtc = _now,
                }
            );
            _ = await seed.SaveChangesAsync();
            ownerLedgerId = ownerLedger.Id;
            otherLedgerId = otherLedger.Id;
        }

        await using (var exportDb = await factory.CreateDbContextAsync())
        {
            var export = await ViewerPrivacyService.ExportAsync(
                exportDb,
                PrivacySubject.Create("owner-id", "other_old"),
                hostId,
                default
            );
            export
                .Sections["viewer-passports.profiles"]
                .Cast<ViewerPassport>()
                .Single()
                .Id.ShouldBe(ownerPassportId);
            export
                .Sections["points.balances"]
                .Cast<PointBalance>()
                .Single()
                .Login.ShouldBe("owner_old");
            var ledger = export.Sections["points.ledger"].Cast<PointLedgerEntry>().Single();
            ledger.Id.ShouldBe(ownerLedgerId);
            ledger.Note.ShouldBe("private owner_old adjustment");
            export.Sections["viewer-passports.attendance-days"].Count.ShouldBe(1);
        }

        await using (var unrelatedDb = await factory.CreateDbContextAsync())
        {
            var unrelated = PrivacySubject.Create("unrelated-id", "owner_old");
            var export = await ViewerPrivacyService.ExportAsync(
                unrelatedDb,
                unrelated,
                hostId,
                default
            );
            export.Sections.ShouldNotContainKey("viewer-passports.profiles");
            export.Sections.ShouldNotContainKey("points.balances");
            export.Sections.ShouldNotContainKey("points.ledger");
            var report = await ViewerPrivacyService.EraseAsync(
                unrelatedDb,
                unrelated,
                hostId,
                default
            );
            report.TotalChangedRows.ShouldBe(0);
        }

        await using (var eraseDb = await factory.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(
                eraseDb,
                PrivacySubject.Create("owner-id", "other_old"),
                hostId,
                default
            );
            report.ChangedRows["points.balances"].ShouldBe(1);
            report.ChangedRows["points.ledger.actor-references"].ShouldBe(1);
            report.ChangedRows["points.ledger.counterparty-references"].ShouldBe(1);
            report.ChangedRows["viewer-passports.profiles"].ShouldBe(1);
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.PointBalances.SingleAsync()).Login.ShouldBe("other_old");
        var erasedLedger = await verify.PointLedgerEntries.SingleAsync(value =>
            value.Id == ownerLedgerId
        );
        erasedLedger.ActorLogin.ShouldBeNull();
        erasedLedger.CounterpartyLogin.ShouldBeNull();
        erasedLedger.Note.ShouldBeEmpty();
        var retainedLedger = await verify.PointLedgerEntries.SingleAsync(value =>
            value.Id == otherLedgerId
        );
        retainedLedger.ActorLogin.ShouldBe("other_old");
        retainedLedger.CounterpartyLogin.ShouldBe("other_new");
        retainedLedger.Note.ShouldBe("private other_old adjustment");
        (await verify.ViewerPassports.SingleAsync()).TwitchUserId.ShouldBe("other-id");
        (await verify.ViewerPassportAttendanceDays.CountAsync()).ShouldBe(0);
        (
            await verify
                .ViewerPassportAmbiguousLogins.OrderBy(value => value.Login)
                .Select(value => value.Login)
                .ToArrayAsync()
        ).ShouldBe(["owner_new", "owner_old"]);
    }

    [Test]
    public async Task AmbiguousOrUnrelatedIdentity_DoesNotEraseBingoEventOrOverlayText()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = Host("channel");
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = seed.ViewerPassportAmbiguousLogins.Add(
                new()
                {
                    HostId = hostId,
                    Login = "victim",
                    DetectedAtUtc = _now,
                }
            );
            await AddBingoPatternRowsAsync(seed, hostId, "victim");
        }

        await using (var erase = await factory.CreateDbContextAsync())
        {
            (
                await ViewerPrivacyService.EraseAsync(
                    erase,
                    PrivacySubject.Create(null, "victim"),
                    hostId,
                    default
                )
            ).TotalChangedRows.ShouldBe(0);
            (
                await ViewerPrivacyService.EraseAsync(
                    erase,
                    PrivacySubject.Create("unrelated-id", "victim"),
                    hostId,
                    default
                )
            ).TotalChangedRows.ShouldBe(0);
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.BingoEvidence.SingleAsync()).Summary.ShouldBe("victim marked a square");
        (await verify.BingoModerationAudit.SingleAsync()).PrivateNote.ShouldBe(
            "private review for victim"
        );
        (await verify.BingoEvents.SingleAsync()).PublicPayload.ShouldContain("victim");
        var overlay = await verify.OverlayEventFeedItems.SingleAsync();
        overlay.SourceKey.ShouldContain("victim");
        overlay.Body.ShouldContain("victim");
        (await verify.ViewerPassportAmbiguousLogins.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task BingoNativeIds_BlockAliasTextFallbackWhileLoginOnlyEventsRemainErasable()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = Host("channel");
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = await AddPassportAsync(seed, hostId, "viewer-id", "viewer", ["viewer"]);
            await AddBingoPatternRowsAsync(seed, hostId, "viewer");
        }

        await using (var erase = await factory.CreateDbContextAsync())
        {
            _ = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create("viewer-id", null),
                hostId,
                default
            );
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.BingoEvidence.SingleAsync()).Summary.ShouldBe("viewer marked a square");
        (await verify.BingoModerationAudit.SingleAsync()).PrivateNote.ShouldBe(
            "private review for viewer"
        );
        (await verify.BingoEvents.CountAsync()).ShouldBe(0);
        (await verify.OverlayEventFeedItems.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task HostlessLoginOnly_RequiresOneStableOwnerAcrossEveryMatchingHost()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int firstHostId;
        int secondHostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var firstHost = Host("first");
            var secondHost = Host("second");
            var thirdHost = Host("third");
            var fourthHost = Host("fourth");
            seed.Hosts.AddRange(firstHost, secondHost, thirdHost, fourthHost);
            _ = await seed.SaveChangesAsync();
            firstHostId = firstHost.Id;
            secondHostId = secondHost.Id;
            _ = await AddPassportAsync(seed, firstHost.Id, "first-owner", "shared", ["shared"]);
            _ = await AddPassportAsync(seed, secondHost.Id, "second-owner", "shared", ["shared"]);
            _ = await AddPassportAsync(seed, thirdHost.Id, "same-owner", "same", ["same"]);
            _ = await AddPassportAsync(seed, fourthHost.Id, "same-owner", "same", ["same"]);
            seed.PointBalances.AddRange(
                Balance(firstHost.Id, "shared", "10"),
                Balance(secondHost.Id, "shared", "20"),
                Balance(thirdHost.Id, "same", "30"),
                Balance(fourthHost.Id, "same", "40")
            );
            _ = await seed.SaveChangesAsync();
        }

        await using (var hostless = await factory.CreateDbContextAsync())
        {
            var collision = await ViewerPrivacyService.ExportAsync(
                hostless,
                PrivacySubject.Create(null, "shared"),
                hostId: null,
                default
            );
            collision.Sections.ShouldBeEmpty();
            (
                await ViewerPrivacyService.EraseAsync(
                    hostless,
                    PrivacySubject.Create(null, "shared"),
                    hostId: null,
                    default
                )
            ).TotalChangedRows.ShouldBe(0);

            var oneOwner = await ViewerPrivacyService.ExportAsync(
                hostless,
                PrivacySubject.Create(null, "same"),
                hostId: null,
                default
            );
            oneOwner.Sections["viewer-passports.profiles"].Count.ShouldBe(2);
            oneOwner
                .Sections["points.balances"]
                .Cast<PointBalance>()
                .Select(value => value.Amount)
                .Order()
                .ToArray()
                .ShouldBe(["30", "40"]);
        }

        await using (var hostScoped = await factory.CreateDbContextAsync())
        {
            var firstOwner = await ViewerPrivacyService.ExportAsync(
                hostScoped,
                PrivacySubject.Create(null, "shared"),
                firstHostId,
                default
            );
            firstOwner
                .Sections["viewer-passports.profiles"]
                .Cast<ViewerPassport>()
                .Single()
                .TwitchUserId.ShouldBe("first-owner");
            firstOwner
                .Sections["points.balances"]
                .Cast<PointBalance>()
                .Single()
                .Amount.ShouldBe("10");
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.PointBalances.CountAsync()).ShouldBe(4);
        (
            await verify.ViewerPassports.CountAsync(value =>
                value.HostId == firstHostId || value.HostId == secondHostId
            )
        ).ShouldBe(2);
    }

    [Test]
    public async Task MarkerWriteLock_MakesExportAndErasureFailClosedWithoutPartialMutation()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = Host("channel");
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = await AddPassportAsync(
                seed,
                hostId,
                "owner-id",
                "owner_new",
                ["owner_old", "owner_new"]
            );
            seed.PointBalances.AddRange(
                Balance(hostId, "owner_old", "10"),
                Balance(hostId, "owner_new", "20")
            );
            _ = await seed.SaveChangesAsync();
        }

        await using var markerWriter = await factory.CreateDbContextAsync();
        await using var markerTransaction = await markerWriter.Database.BeginTransactionAsync();
        _ = markerWriter.ViewerPassportAmbiguousLogins.Add(
            new()
            {
                HostId = hostId,
                Login = "owner_old",
                DetectedAtUtc = _now,
            }
        );
        _ = await markerWriter.SaveChangesAsync();

        await using (var request = await factory.CreateDbContextAsync())
        {
            SetShortBusyTimeout(request);
            var export = await ViewerPrivacyService.ExportAsync(
                request,
                PrivacySubject.Create(null, "owner_old"),
                hostId,
                default
            );
            export.Sections.ShouldBeEmpty();
            (
                await ViewerPrivacyService.EraseAsync(
                    request,
                    PrivacySubject.Create(null, "owner_old"),
                    hostId,
                    default
                )
            ).TotalChangedRows.ShouldBe(0);
        }

        await markerTransaction.CommitAsync();
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ViewerPassports.CountAsync()).ShouldBe(1);
        (
            await verify
                .ViewerPassportLogins.OrderBy(value => value.Login)
                .Select(value => value.Login)
                .ToArrayAsync()
        ).ShouldBe(["owner_new", "owner_old"]);
        (
            await verify
                .PointBalances.OrderBy(value => value.Login)
                .Select(value => value.Login)
                .ToArrayAsync()
        ).ShouldBe(["owner_new", "owner_old"]);
    }

    [Test]
    public async Task ExistingSerializableTransaction_ExportsOnePreMarkerSnapshot()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = Host("channel");
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = await AddPassportAsync(
                seed,
                hostId,
                "owner-id",
                "owner_new",
                ["owner_old", "owner_new"]
            );
            seed.PointBalances.AddRange(
                Balance(hostId, "owner_old", "10"),
                Balance(hostId, "owner_new", "20")
            );
            _ = await seed.SaveChangesAsync();
        }

        await using (var request = await factory.CreateDbContextAsync())
        {
            await using var transaction = await request.Database.BeginTransactionAsync();
            await using var markerWriter = await factory.CreateDbContextAsync();
            SetShortBusyTimeout(markerWriter);
            _ = markerWriter.ViewerPassportAmbiguousLogins.Add(
                new()
                {
                    HostId = hostId,
                    Login = "owner_old",
                    DetectedAtUtc = _now,
                }
            );
            var exception = await Should.ThrowAsync<DbUpdateException>(async () =>
                _ = await markerWriter.SaveChangesAsync()
            );
            exception
                .InnerException.ShouldBeOfType<SqliteException>()
                .SqliteErrorCode.ShouldBeOneOf(
                    SQLitePCL.raw.SQLITE_BUSY,
                    SQLitePCL.raw.SQLITE_LOCKED
                );

            var export = await ViewerPrivacyService.ExportAsync(
                request,
                PrivacySubject.Create(null, "owner_old"),
                hostId,
                default
            );
            export.Sections["viewer-passports.profiles"].Count.ShouldBe(1);
            export
                .Sections["points.balances"]
                .Cast<PointBalance>()
                .Select(value => value.Login)
                .Order()
                .ToArray()
                .ShouldBe(["owner_new", "owner_old"]);
            await transaction.CommitAsync();
        }

        await using (var markerWriter = await factory.CreateDbContextAsync())
        {
            _ = markerWriter.ViewerPassportAmbiguousLogins.Add(
                new()
                {
                    HostId = hostId,
                    Login = "owner_old",
                    DetectedAtUtc = _now,
                }
            );
            _ = await markerWriter.SaveChangesAsync();
        }

        await using var afterMarker = await factory.CreateDbContextAsync();
        (
            await ViewerPrivacyService.ExportAsync(
                afterMarker,
                PrivacySubject.Create(null, "owner_old"),
                hostId,
                default
            )
        ).Sections.ShouldBeEmpty();
        var stableExport = await ViewerPrivacyService.ExportAsync(
            afterMarker,
            PrivacySubject.Create("owner-id", null),
            hostId,
            default
        );
        stableExport
            .Sections["points.balances"]
            .Cast<PointBalance>()
            .Single()
            .Login.ShouldBe("owner_new");
    }

    [Test]
    public async Task AmbientErasure_UsesSavepointWithoutCommittingTheCallerTransaction()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = Host("channel");
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = await AddPassportAsync(seed, hostId, "owner-id", "owner", ["owner"]);
            _ = seed.PointBalances.Add(Balance(hostId, "owner", "10"));
            _ = await seed.SaveChangesAsync();
        }

        await using (var erase = await factory.CreateDbContextAsync())
        {
            await using var transaction = await erase.Database.BeginTransactionAsync();
            var report = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create("owner-id", null),
                hostId,
                default
            );
            report.ChangedRows["points.balances"].ShouldBe(1);
            (await erase.PointBalances.CountAsync()).ShouldBe(0);
            (await erase.ViewerPassports.CountAsync()).ShouldBe(0);
            await transaction.RollbackAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.PointBalances.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassports.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassportAmbiguousLogins.CountAsync()).ShouldBe(0);
    }

    private static async Task<ViewerPassport> AddPassportAsync(
        BlokeBotDbContext db,
        int hostId,
        string twitchUserId,
        string currentLogin,
        IReadOnlyCollection<string> aliases
    )
    {
        var passport = new ViewerPassport
        {
            HostId = hostId,
            TwitchUserId = twitchUserId,
            Login = currentLogin,
            DisplayName = currentLogin,
            CreatedAtUtc = _now,
            UpdatedAtUtc = _now,
        };
        _ = db.ViewerPassports.Add(passport);
        _ = await db.SaveChangesAsync();
        db.ViewerPassportLogins.AddRange(
            aliases.Select(login => new ViewerPassportLogin
            {
                HostId = hostId,
                PassportId = passport.Id,
                Login = login,
                FirstSeenAtUtc = _now,
                LastSeenAtUtc = _now,
            })
        );
        _ = await db.SaveChangesAsync();
        return passport;
    }

    private static async Task AddBingoPatternRowsAsync(
        BlokeBotDbContext db,
        int hostId,
        string login
    )
    {
        var template = new BingoTemplate
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            Name = "Privacy template",
            CurrentRevision = 1,
            CreatedAtUtc = _now,
            UpdatedAtUtc = _now,
        };
        _ = db.BingoTemplates.Add(template);
        _ = await db.SaveChangesAsync();
        var revision = new BingoTemplateRevision
        {
            HostId = hostId,
            OperationId = Guid.NewGuid(),
            TemplateId = template.Id,
            Revision = 1,
            Dimension = 3,
            CreatedByTwitchUserId = "moderator-id",
            CreatedByLogin = "moderator",
            CreatedAtUtc = _now,
        };
        _ = db.BingoTemplateRevisions.Add(revision);
        _ = await db.SaveChangesAsync();
        var game = new BingoGame
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            TemplateRevisionId = revision.Id,
            TemplateName = template.Name,
            TemplateRevisionNumber = 1,
            Dimension = 3,
            Seed = "privacy",
            Mode = BingoGameMode.Shared,
            Status = BingoGameStatus.Issued,
            CreatedAtUtc = _now,
        };
        _ = db.BingoGames.Add(game);
        _ = await db.SaveChangesAsync();
        var card = new BingoCard
        {
            HostId = hostId,
            GameId = game.Id,
            PublicId = Guid.NewGuid(),
            AssignmentKey = "shared",
            AssignmentName = "Shared",
            IssuedAtUtc = _now,
        };
        _ = db.BingoCards.Add(card);
        _ = await db.SaveChangesAsync();
        var mark = new BingoMark
        {
            HostId = hostId,
            GameId = game.Id,
            CardId = card.Id,
            SquareKey = "square-1",
            Position = 0,
            IsActive = true,
            FirstMarkedAtUtc = _now,
            ChangedAtUtc = _now,
        };
        _ = db.BingoMarks.Add(mark);
        _ = await db.SaveChangesAsync();
        _ = db.BingoEvidence.Add(
            new BingoEvidence
            {
                HostId = hostId,
                GameId = game.Id,
                CardId = card.Id,
                MarkId = mark.Id,
                Action = BingoEvidenceAction.Marked,
                Source = BingoEvidenceSource.Manual,
                EventKind = BingoSquareKind.Manual,
                Summary = $"{login} marked a square",
                ParticipantTwitchUserId = "someone-else-id",
                ParticipantLogin = "someone_else",
                ParticipantDisplayName = "Someone Else",
                OccurredAtUtc = _now,
                RecordedAtUtc = _now,
            }
        );
        _ = db.BingoModerationAudit.Add(
            new BingoModerationAudit
            {
                HostId = hostId,
                GameId = game.Id,
                OperationId = Guid.NewGuid(),
                Action = "review",
                ActorTwitchUserId = "moderator-id",
                ActorLogin = "moderator",
                PrivateNote = $"private review for {login}",
                OccurredAtUtc = _now,
            }
        );
        _ = db.BingoEvents.Add(
            new BingoDomainEvent
            {
                HostId = hostId,
                GameId = game.Id,
                Kind = BingoDomainEventKind.SquareMarked,
                OperationKey = $"bingo-{login}",
                PublicPayload = $$"""{"viewer":"{{login}}"}""",
                OccurredAtUtc = _now,
            }
        );
        var overlay = new OverlayInstance
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            Name = "Privacy feed",
            Type = OverlayType.EventFeed,
            IsEnabled = true,
            ConfigurationJson = "{}",
            AccessKeyDigest = new byte[32],
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = _now,
            UpdatedAtUtc = _now,
        };
        _ = db.OverlayInstances.Add(overlay);
        _ = db.OverlayEventFeedItems.Add(
            new OverlayEventFeedItem
            {
                OverlayInstance = overlay,
                HostId = hostId,
                Kind = OverlayEventFeedKind.BingoEvent,
                SourceKey = $"bingo-{login}",
                Priority = OverlayEventFeedPriority.Normal,
                Lifecycle = OverlayEventFeedLifecycle.Queued,
                Title = "Bingo",
                Body = $"{login} marked a square",
                DurationSeconds = 5,
                EnqueuedAtUtc = _now,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static BotHost Host(string login) =>
        new()
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            CreatedAtUtc = _now,
        };

    private static PointBalance Balance(int hostId, string login, string amount) =>
        new()
        {
            HostId = hostId,
            Login = login,
            Amount = amount,
            UpdatedAtUtc = _now,
        };

    private static void SetShortBusyTimeout(BlokeBotDbContext db) =>
        ((SqliteConnection)db.Database.GetDbConnection()).DefaultTimeout = 1;
}
