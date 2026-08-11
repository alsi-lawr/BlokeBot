using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
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
}
