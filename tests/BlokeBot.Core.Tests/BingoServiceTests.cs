using System.Text.Json;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BingoServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public void CardLayout_ReproducesEverySupportedGridAndDerivesWinLines(int size)
    {
        var dimension = new BingoDimension(size);
        var keys = Enumerable
            .Range(1, dimension.SquareCount)
            .Select(value => new BingoSquareKey($"s-{value}"))
            .ToArray();

        var first = BingoCardLayout.Generate("seed-42", 7, dimension, "card:one", keys);
        var replay = BingoCardLayout.Generate("seed-42", 7, dimension, "card:one", keys.Reverse());
        var otherAssignment = BingoCardLayout.Generate("seed-42", 7, dimension, "card:two", keys);

        replay.ShouldBe(first);
        otherAssignment.ShouldNotBe(first);
        first.Distinct().Count().ShouldBe(dimension.SquareCount);
        var lines = BingoCardLayout.WinLines(dimension, includeFullCard: true);
        lines
            .Single(value => value.Kind == BingoWinKind.FullCard)
            .Positions.Count.ShouldBe(dimension.SquareCount);
        lines
            .Where(value => value.Kind != BingoWinKind.FullCard)
            .ShouldAllBe(value => value.Positions.Count == size);
    }

    [Test]
    public async Task TeamRoster_EnforcesCapsAndFreezesAssignmentsAtIssue()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var service = CreateService(database);
        var template = await ConfigureTemplateAsync(database, service, hostId, 3, ManualSquares(3));
        _ = Success(
            await service.CreateGameAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    template.Id,
                    BingoGameMode.Team,
                    "team-seed",
                    2,
                    2,
                    ["Aurora", "Nebula"],
                    Actor()
                ),
                default
            )
        );
        var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        var aurora = game.Teams.Single(value => value.Name == "Aurora");
        var nebula = game.Teams.Single(value => value.Name == "Nebula");
        var one = Viewer("one");
        var two = Viewer("two");
        _ = Success(await service.JoinAsync(hostId, Roster(game, one, aurora.Id), default));
        _ = Success(await service.JoinAsync(hostId, Roster(game, two, aurora.Id), default));
        _ = (
            await service.JoinAsync(hostId, Roster(game, Viewer("three"), nebula.Id), default)
        ).ShouldBeOfType<BingoOperationOutcome.Invalid>();
        _ = Success(
            await service.MoveAsync(
                hostId,
                Roster(game, two, nebula.Id) with
                {
                    OperationId = Guid.NewGuid(),
                },
                default
            )
        );
        _ = Success(await service.IssueAsync(hostId, Action(game), default));

        var issued = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        issued.Status.ShouldBe(BingoGameStatus.Issued);
        issued.Cards.Count.ShouldBe(2);
        issued
            .Cards.Single(value => value.AssignmentName == "Aurora")
            .Participants.Single()
            .Login.ShouldBe("one");
        issued
            .Cards.Single(value => value.AssignmentName == "Nebula")
            .Participants.Single()
            .Login.ShouldBe("two");
        _ = (
            await service.RemoveAsync(
                hostId,
                Roster(issued, one, null) with
                {
                    OperationId = Guid.NewGuid(),
                },
                default
            )
        ).ShouldBeOfType<BingoOperationOutcome.Frozen>();

        var originalLayout = issued
            .Cards.Single(value => value.AssignmentName == "Aurora")
            .Squares.Select(value => value.Key)
            .ToArray();
        _ = Success(
            await service.SaveTemplateAsync(
                hostId,
                TemplateDraft(3, ManualSquares(3).Reverse().ToArray(), template.Id),
                default
            )
        );
        var afterEdit = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        afterEdit
            .Cards.Single(value => value.AssignmentName == "Aurora")
            .Squares.Select(value => value.Key)
            .ShouldBe(originalLayout);
    }

    [Test]
    public async Task IssueFailure_LeavesTheJoiningRosterUnissuedAndTheSameCommandCanRetry()
    {
        var failure = new FailFirstIssuedGameSaveInterceptor();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(failure);
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var service = CreateService(database);
        var template = await ConfigureTemplateAsync(database, service, hostId, 3, ManualSquares(3));
        _ = Success(
            await service.CreateGameAsync(
                hostId,
                SharedGame(template.Id) with
                {
                    Mode = BingoGameMode.UniquePerViewer,
                },
                default
            )
        );
        var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        _ = Success(await service.JoinAsync(hostId, Roster(game, Viewer("one"), null), default));
        _ = Success(await service.JoinAsync(hostId, Roster(game, Viewer("two"), null), default));
        var issue = Action(game);

        _ = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.IssueAsync(hostId, issue, default)
        );

        await using (var failed = await database.CreateDbContextAsync())
        {
            var retained = await failed
                .BingoGames.Include(value => value.Cards)
                .Include(value => value.Participants)
                .SingleAsync();
            retained.Status.ShouldBe(BingoGameStatus.Joining);
            retained.Cards.ShouldBeEmpty();
            retained.Participants.ShouldAllBe(value => value.CardId == null);
            (
                await failed.BingoModerationAudit.AnyAsync(value => value.Action == "issue")
            ).ShouldBeFalse();
            (
                await failed.BingoEvents.AnyAsync(value =>
                    value.Kind == BingoDomainEventKind.GameIssued
                )
            ).ShouldBeFalse();
        }

        _ = Success(await service.IssueAsync(hostId, issue, default));

        await using var retried = await database.CreateDbContextAsync();
        var issued = await retried
            .BingoGames.Include(value => value.Cards)
            .Include(value => value.Participants)
            .SingleAsync();
        issued.Status.ShouldBe(BingoGameStatus.Issued);
        issued.Cards.Count.ShouldBe(issued.Participants.Count);
        issued.Participants.ShouldAllBe(value => value.CardId != null);
        (await retried.BingoModerationAudit.CountAsync(value => value.Action == "issue")).ShouldBe(
            1
        );
        (
            await retried.BingoEvents.CountAsync(value =>
                value.Kind == BingoDomainEventKind.GameIssued
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentServiceInstances_AllowOnlyOneActiveGameForAHost()
    {
        var synchronization = new SynchronizeFirstGameCreatesInterceptor();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(synchronization);
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var first = CreateService(database);
        var second = CreateService(database);
        var template = await ConfigureTemplateAsync(database, first, hostId, 3, ManualSquares(3));

        var outcomes = await Task.WhenAll(
            first.CreateGameAsync(hostId, SharedGame(template.Id), default),
            second.CreateGameAsync(
                hostId,
                SharedGame(template.Id) with
                {
                    OperationId = Guid.NewGuid(),
                    Seed = "other-seed",
                },
                default
            )
        );

        outcomes.OfType<BingoOperationOutcome.Succeeded>().Count().ShouldBe(1);
        outcomes.OfType<BingoOperationOutcome.Conflict>().Count().ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.BingoGames.CountAsync(value =>
                value.Status == BingoGameStatus.Joining || value.Status == BingoGameStatus.Issued
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentServiceInstances_EnforceTheOptionalParticipantCap()
    {
        var synchronization = new SynchronizeFirstParticipantJoinsInterceptor();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(synchronization);
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var first = CreateService(database);
        var second = CreateService(database);
        var template = await ConfigureTemplateAsync(database, first, hostId, 3, ManualSquares(3));
        _ = Success(
            await first.CreateGameAsync(
                hostId,
                SharedGame(template.Id) with
                {
                    Mode = BingoGameMode.UniquePerViewer,
                    ParticipantCap = 1,
                },
                default
            )
        );
        var game = (await first.GetModeratorGamesAsync(hostId, default)).Single().Game;

        var outcomes = await Task.WhenAll(
            first.JoinAsync(hostId, Roster(game, Viewer("one"), null), default),
            second.JoinAsync(hostId, Roster(game, Viewer("two"), null), default)
        );

        outcomes.OfType<BingoOperationOutcome.Succeeded>().Count().ShouldBe(1);
        outcomes.OfType<BingoOperationOutcome.Invalid>().Count().ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.BingoParticipants.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task AutomaticEvents_AreDurableHostScopedAndStoreSparseNormalizedEvidence()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var secondHost = await SeedHostAsync(database, "beta", HostFeatureFlags.Bingo);
        var overlay = new RecordingBingoOverlayEvents();
        var service = CreateService(database, overlay: overlay);
        foreach (var hostId in new[] { firstHost, secondHost })
        {
            var squares = Enumerable
                .Range(1, 9)
                .Select(value =>
                    (BingoSquareDefinition)
                        new BingoSquareDefinition.IncomingRaid(
                            new($"raid-{value}"),
                            $"Raid {value}",
                            10
                        )
                )
                .ToArray();
            var template = await ConfigureTemplateAsync(database, service, hostId, 3, squares);
            _ = Success(await service.CreateGameAsync(hostId, SharedGame(template.Id), default));
            var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
            _ = Success(await service.IssueAsync(hostId, Action(game), default));
        }
        var raid = new BingoAutomaticEvent.IncomingRaid(
            "provider-message-42",
            Viewer("raider"),
            42,
            _now
        );

        _ = Success(await service.ProcessEventAsync(firstHost, raid, default));
        Success(await service.ProcessEventAsync(firstHost, raid, default))
            .WasIdempotent.ShouldBeTrue();
        _ = Success(await service.ProcessEventAsync(secondHost, raid, default));

        await using var verify = await database.CreateDbContextAsync();
        (await verify.BingoEventReceipts.CountAsync()).ShouldBe(2);
        (await verify.BingoMarks.CountAsync(value => value.HostId == firstHost)).ShouldBe(9);
        (await verify.BingoEvidence.CountAsync(value => value.HostId == firstHost)).ShouldBe(9);
        (await verify.BingoCards.CountAsync(value => value.HostId == firstHost)).ShouldBe(1);
        var evidence = await verify.BingoEvidence.FirstAsync(value => value.HostId == firstHost);
        evidence.ParticipantLogin.ShouldBe("raider");
        evidence.Summary.ShouldContain("42 viewers");
        (
            await verify.BingoEvents.AnyAsync(value =>
                value.Kind == BingoDomainEventKind.SquareMarked
            )
        ).ShouldBeTrue();
        overlay.Events.Count(value => value.Kind == BingoDomainEventKind.SquareMarked).ShouldBe(2);
    }

    [Test]
    public async Task ManualCorrection_PreservesWinAndGrantsEachRewardOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var features =
            HostFeatureFlags.Bingo
            | HostFeatureFlags.Points
            | HostFeatureFlags.CommunityProgression;
        var hostId = await SeedHostAsync(database, "alpha", features);
        await SeedExternalAchievementAsync(database, hostId, "bingo-winner");
        var grants = new RecordingAchievementGrants();
        var service = CreateService(database, grants);
        var template = await ConfigureTemplateAsync(
            database,
            service,
            hostId,
            3,
            ManualSquares(3),
            new(new PointAmount(25), new("bingo-winner"))
        );
        _ = Success(
            await service.CreateGameAsync(
                hostId,
                SharedGame(template.Id) with
                {
                    Mode = BingoGameMode.UniquePerViewer,
                },
                default
            )
        );
        var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        var viewer = Viewer("winner");
        _ = Success(await service.JoinAsync(hostId, Roster(game, viewer, null), default));
        _ = Success(await service.IssueAsync(hostId, Action(game), default));
        game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        var card = game.Cards.Single();
        for (var position = 0; position < 3; position++)
        {
            _ = Success(
                await service.ConfirmManualAsync(
                    hostId,
                    Mark(game, card, position, $"private-note-{position}"),
                    default
                )
            );
        }
        _ = Success(
            await service.ReverseManualAsync(
                hostId,
                Mark(game, card, 2, "private-correction") with
                {
                    OperationId = Guid.NewGuid(),
                },
                default
            )
        );
        _ = Success(
            await service.ConfirmManualAsync(
                hostId,
                Mark(game, card, 2, "private-reconfirm") with
                {
                    OperationId = Guid.NewGuid(),
                },
                default
            )
        );

        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.BingoWins.CountAsync()).ShouldBe(1);
            (
                await verify.PointLedgerEntries.CountAsync(value =>
                    value.Kind == PointLedgerKind.BingoReward
                )
            ).ShouldBe(1);
            (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("25");
            (
                await verify.BingoEvidence.CountAsync(value =>
                    value.Action == BingoEvidenceAction.Reversed
                )
            ).ShouldBe(1);
            (
                await verify.BingoModerationAudit.CountAsync(value =>
                    value.PrivateNote.StartsWith("private-")
                )
            ).ShouldBe(5);
        }
        grants.Requests.Count.ShouldBe(1);
        var publicView = (await service.GetPublicAsync("alpha", default)).ShouldNotBeNull();
        var publicJson = JsonSerializer.Serialize(publicView);
        publicJson.ShouldNotContain("private-correction");
        publicView.LiveGame!.Cards.Single().Wins.Count.ShouldBe(1);
        publicView.LiveGame.Cards.Single().Squares[2].Marked.ShouldBeTrue();
        publicView
            .LiveGame.Cards.Single()
            .Squares[2]
            .Evidence.Select(value => value.Action)
            .ShouldBe([
                BingoEvidenceAction.Marked,
                BingoEvidenceAction.Reversed,
                BingoEvidenceAction.Marked,
            ]);
    }

    [Test]
    public async Task ViewerErasure_RemovesUniqueCardAndDerivedPublicEventIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var service = CreateService(database);
        var squares = Enumerable
            .Range(1, 9)
            .Select(value =>
                (BingoSquareDefinition)
                    new BingoSquareDefinition.IncomingRaid(new($"raid-{value}"), $"Raid {value}", 1)
            )
            .ToArray();
        var template = await ConfigureTemplateAsync(database, service, hostId, 3, squares);
        _ = Success(
            await service.CreateGameAsync(
                hostId,
                SharedGame(template.Id) with
                {
                    Mode = BingoGameMode.UniquePerViewer,
                },
                default
            )
        );
        var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        var alice = new BingoViewer("alice-id", "alice", "Alice Display");
        var bob = new BingoViewer("bob-id", "bob", "Bob Display");
        _ = Success(
            await service.JoinAsync(
                hostId,
                Roster(game, alice, null) with
                {
                    PrivateNote = "Alice Display verified before issue.",
                },
                default
            )
        );
        _ = Success(await service.JoinAsync(hostId, Roster(game, bob, null), default));
        _ = Success(await service.IssueAsync(hostId, Action(game), default));
        _ = Success(
            await service.ProcessEventAsync(
                hostId,
                new BingoAutomaticEvent.IncomingRaid("raid-alice", alice, 12, _now),
                default
            )
        );
        game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        _ = Success(await service.ArchiveAsync(hostId, Action(game), default));

        _ = Success(
            await service.CreateGameAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    template.Id,
                    BingoGameMode.Team,
                    "team-privacy",
                    null,
                    1,
                    ["Team Aurora"],
                    Actor()
                ),
                default
            )
        );
        var teamGame = (await service.GetModeratorGamesAsync(hostId, default))
            .Single(value => value.Game.Status == BingoGameStatus.Joining)
            .Game;
        var team = teamGame.Teams.Single();
        _ = Success(await service.JoinAsync(hostId, Roster(teamGame, alice, team.Id), default));
        _ = Success(await service.JoinAsync(hostId, Roster(teamGame, bob, team.Id), default));
        _ = Success(await service.IssueAsync(hostId, Action(teamGame), default));
        teamGame = (await service.GetModeratorGamesAsync(hostId, default))
            .Single(value => value.Game.Status == BingoGameStatus.Issued)
            .Game;
        _ = Success(await service.ArchiveAsync(hostId, Action(teamGame), default));

        _ = Success(
            await service.CreateGameAsync(
                hostId,
                SharedGame(template.Id) with
                {
                    OperationId = Guid.NewGuid(),
                    Seed = "shared-privacy",
                },
                default
            )
        );
        var sharedGame = (await service.GetModeratorGamesAsync(hostId, default))
            .Single(value => value.Game.Status == BingoGameStatus.Joining)
            .Game;
        _ = Success(await service.JoinAsync(hostId, Roster(sharedGame, alice, null), default));
        _ = Success(await service.JoinAsync(hostId, Roster(sharedGame, bob, null), default));
        _ = Success(await service.IssueAsync(hostId, Action(sharedGame), default));
        sharedGame = (await service.GetModeratorGamesAsync(hostId, default))
            .Single(value => value.Game.Status == BingoGameStatus.Issued)
            .Game;
        _ = Success(await service.ArchiveAsync(hostId, Action(sharedGame), default));

        var beforeErasure = (await service.GetPublicAsync("alpha", default))!;
        var uniqueCardBeforeErasure = beforeErasure
            .Archive.Single(value => value.Mode == BingoGameMode.UniquePerViewer)
            .Cards.Single(value =>
                value.Participants.Any(participant => participant.Login == alice.Login)
            );
        var issuedLayout = uniqueCardBeforeErasure.Squares.Select(value => value.Key).ToArray();
        string assignmentKeyBeforeErasure;
        await using (var inspect = await database.CreateDbContextAsync())
        {
            assignmentKeyBeforeErasure = await inspect
                .BingoCards.Where(value => value.PublicId == uniqueCardBeforeErasure.Id.Value)
                .Select(value => value.AssignmentKey)
                .SingleAsync();
        }
        foreach (var identity in new[] { alice.TwitchUserId, alice.Login, alice.DisplayName })
        {
            assignmentKeyBeforeErasure.ShouldNotContain(identity, Case.Insensitive);
        }

        await using (var seed = await database.CreateDbContextAsync())
        {
            var overlay = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                Name = "Bingo feed",
                Type = OverlayType.EventFeed,
                IsEnabled = true,
                ConfigurationJson = "{}",
                AccessKeyDigest = new byte[32],
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = _now.UtcDateTime,
                UpdatedAtUtc = _now.UtcDateTime,
            };
            _ = seed.OverlayInstances.Add(overlay);
            _ = seed.OverlayEventFeedItems.Add(
                new OverlayEventFeedItem
                {
                    OverlayInstance = overlay,
                    HostId = hostId,
                    Kind = OverlayEventFeedKind.BingoEvent,
                    SourceKey = "bingo-alice-privacy",
                    Priority = OverlayEventFeedPriority.Normal,
                    Lifecycle = OverlayEventFeedLifecycle.Queued,
                    Title = "Bingo",
                    Body = "Alice Display (@alice) completed a row",
                    DurationSeconds = 5,
                    EnqueuedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        await using (var erase = await database.CreateDbContextAsync())
        {
            _ = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create(alice.TwitchUserId, alice.Login),
                hostId,
                default
            );
        }

        var publicView = await service.GetPublicAsync("alpha", default);
        var uniqueArchive = publicView!.Archive.Single(value =>
            value.Mode == BingoGameMode.UniquePerViewer
        );
        var uniqueCardAfterErasure = uniqueArchive.Cards.Single(value =>
            value.Id == uniqueCardBeforeErasure.Id
        );
        uniqueCardAfterErasure.AssignmentName.ShouldBe("[erased]");
        uniqueCardAfterErasure.Squares.Select(value => value.Key).ShouldBe(issuedLayout);
        uniqueArchive
            .Cards.Single(value =>
                value.Participants.Any(participant => participant.Login == bob.Login)
            )
            .AssignmentName.ShouldBe(bob.DisplayName);
        publicView
            .Archive.Single(value => value.Mode == BingoGameMode.Team)
            .Cards.Single()
            .AssignmentName.ShouldBe("Team Aurora");
        publicView
            .Archive.Single(value => value.Mode == BingoGameMode.Shared)
            .Cards.Single()
            .AssignmentName.ShouldBe("Everyone");
        var publicJson = JsonSerializer.Serialize(publicView);
        foreach (var identity in new[] { alice.TwitchUserId, alice.Login, alice.DisplayName })
        {
            publicJson.ShouldNotContain(identity, Case.Insensitive);
        }

        await using var verify = await database.CreateDbContextAsync();
        (
            await verify
                .BingoCards.Where(value => value.PublicId == uniqueCardBeforeErasure.Id.Value)
                .Select(value => value.AssignmentKey)
                .SingleAsync()
        ).ShouldBe(assignmentKeyBeforeErasure);
        var retainedText = string.Join(
            '\n',
            (
                await verify
                    .BingoCards.Select(value =>
                        value.AssignmentKey
                        + value.AssignmentName
                        + (value.IssuedLayout ?? string.Empty)
                    )
                    .ToArrayAsync()
            )
                .Concat(
                    await verify
                        .BingoEvents.Select(value => value.OperationKey + value.PublicPayload)
                        .ToArrayAsync()
                )
                .Concat(
                    await verify
                        .BingoEvidence.Select(value =>
                            value.Summary
                            + (value.ParticipantTwitchUserId ?? string.Empty)
                            + (value.ParticipantLogin ?? string.Empty)
                            + (value.ParticipantDisplayName ?? string.Empty)
                        )
                        .ToArrayAsync()
                )
                .Concat(
                    await verify
                        .BingoWinRecipients.Select(value =>
                            value.TwitchUserId + value.Login + value.DisplayName
                        )
                        .ToArrayAsync()
                )
                .Concat(
                    await verify
                        .BingoModerationAudit.Select(value =>
                            value.ActorTwitchUserId + value.ActorLogin + value.PrivateNote
                        )
                        .ToArrayAsync()
                )
                .Concat(
                    await verify
                        .OverlayEventFeedItems.Select(value =>
                            value.SourceKey + value.Title + value.Body
                        )
                        .ToArrayAsync()
                )
        );
        foreach (var identity in new[] { alice.TwitchUserId, alice.Login, alice.DisplayName })
        {
            retainedText.ShouldNotContain(identity, Case.Insensitive);
        }
    }

    [Test]
    public async Task WinRewardCapacityFailure_RollsBackTriggeringMarkAndWinAtomically()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(
            database,
            "alpha",
            HostFeatureFlags.Bingo | HostFeatureFlags.Points
        );
        var service = CreateService(database);
        var template = await ConfigureTemplateAsync(
            database,
            service,
            hostId,
            3,
            ManualSquares(3),
            new(new PointAmount(1), null)
        );
        _ = Success(
            await service.CreateGameAsync(
                hostId,
                SharedGame(template.Id) with
                {
                    Mode = BingoGameMode.UniquePerViewer,
                },
                default
            )
        );
        var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        var viewer = Viewer("full");
        _ = Success(await service.JoinAsync(hostId, Roster(game, viewer, null), default));
        _ = Success(await service.IssueAsync(hostId, Action(game), default));
        game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        var card = game.Cards.Single();
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = viewer.Login,
                    Amount = PointAmount.MaximumValue.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        _ = Success(
            await service.ConfirmManualAsync(hostId, Mark(game, card, 0, "first"), default)
        );
        _ = Success(
            await service.ConfirmManualAsync(hostId, Mark(game, card, 1, "second"), default)
        );

        _ = (
            await service.ConfirmManualAsync(hostId, Mark(game, card, 2, "trigger"), default)
        ).ShouldBeOfType<BingoOperationOutcome.Conflict>();

        await using var verify = await database.CreateDbContextAsync();
        (await verify.BingoMarks.CountAsync()).ShouldBe(2);
        (await verify.BingoWins.CountAsync()).ShouldBe(0);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BingoReward
            )
        ).ShouldBe(0);
    }

    [Test]
    public async Task DisabledGate_PreservesIssuedStateAndDoesNotReplaySuppressedEvents()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var service = CreateService(database);
        var squares = Enumerable
            .Range(1, 9)
            .Select(value =>
                (BingoSquareDefinition)
                    new BingoSquareDefinition.IncomingRaid(new($"raid-{value}"), $"Raid {value}", 1)
            )
            .ToArray();
        var template = await ConfigureTemplateAsync(database, service, hostId, 3, squares);
        _ = Success(await service.CreateGameAsync(hostId, SharedGame(template.Id), default));
        var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        _ = Success(await service.IssueAsync(hostId, Action(game), default));
        await SetFeatureAsync(database, hostId, HostFeatureFlags.None, _now.UtcDateTime);
        var suppressed = new BingoAutomaticEvent.IncomingRaid(
            "suppressed",
            Viewer("raider"),
            5,
            _now
        );

        _ = (
            await service.ProcessEventAsync(hostId, suppressed, default)
        ).ShouldBeOfType<BingoOperationOutcome.FeatureDisabled>();
        await SetFeatureAsync(
            database,
            hostId,
            HostFeatureFlags.Bingo,
            _now.AddMinutes(1).UtcDateTime
        );
        _ = Success(await service.ProcessEventAsync(hostId, suppressed, default));
        _ = Success(
            await service.ProcessEventAsync(
                hostId,
                new BingoAutomaticEvent.IncomingRaid(
                    "new",
                    Viewer("raider"),
                    5,
                    _now.AddMinutes(2)
                ),
                default
            )
        );

        await using var verify = await database.CreateDbContextAsync();
        (await verify.BingoTemplates.CountAsync()).ShouldBe(1);
        (await verify.BingoGames.CountAsync()).ShouldBe(1);
        (await verify.BingoEventReceipts.CountAsync()).ShouldBe(1);
        (await verify.BingoMarks.CountAsync()).ShouldBe(9);
    }

    [Test]
    public async Task EventSubRequirementsAndFeatureChanges_FollowOnlyTheBingoGate()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Bingo);
        var service = CreateService(database);
        var runtime = new BingoRuntime(database, service, NullLogger<BingoRuntime>.Instance);
        (
            await runtime.RequiresAsync(
                "alpha",
                AutomationEventSubRequirement.ChannelUpdates,
                default
            )
        ).ShouldBeTrue();
        (
            await runtime.RequiresAsync(
                "alpha",
                AutomationEventSubRequirement.IncomingRaids,
                default
            )
        ).ShouldBeTrue();
        (
            await runtime.RequiresAsync("alpha", AutomationEventSubRequirement.Cheers, default)
        ).ShouldBeFalse();
        var trigger = new RecordingReconciliationTrigger();
        var features = new HostFeatureService(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [],
            [new BingoFeatureObserver(service, trigger)],
            new ManualTimeProvider(_now)
        );

        await features.DisableAsync(hostId, HostFeatureFlags.Bingo, default);
        trigger.Calls.ShouldBe(1);
        (
            await runtime.RequiresAsync(
                "alpha",
                AutomationEventSubRequirement.ChannelUpdates,
                default
            )
        ).ShouldBeFalse();
        await features.EnableAsync(hostId, HostFeatureFlags.Bingo, default);
        trigger.Calls.ShouldBe(2);
    }

    private static BingoService CreateService(
        SqliteBlokeBotDbFactory database,
        ICommunityAchievementGrantService? grants = null,
        IBingoOverlayEventObserver? overlay = null
    ) =>
        new(
            database,
            grants ?? new RecordingAchievementGrants(),
            TestEventBus.Create<AppEventKind>(),
            new ManualTimeProvider(_now),
            overlay is null ? [] : [overlay]
        );

    private static async Task<BingoTemplateView> ConfigureTemplateAsync(
        SqliteBlokeBotDbFactory database,
        BingoService service,
        int hostId,
        int dimension,
        IReadOnlyList<BingoSquareDefinition> squares,
        BingoWinReward? lineReward = null
    )
    {
        _ = Success(
            await service.SaveTemplateAsync(
                hostId,
                TemplateDraft(dimension, squares, null, lineReward),
                default
            )
        );
        return (await service.GetTemplatesAsync(hostId, default)).Single();
    }

    private static BingoTemplateDraft TemplateDraft(
        int dimension,
        IReadOnlyList<BingoSquareDefinition> squares,
        BingoTemplateId? templateId = null,
        BingoWinReward? lineReward = null
    ) =>
        new(
            Guid.NewGuid(),
            templateId,
            "Representative template",
            new(dimension),
            squares,
            false,
            lineReward ?? BingoWinReward.None,
            BingoWinReward.None,
            Actor()
        );

    private static IReadOnlyList<BingoSquareDefinition> ManualSquares(int dimension) =>
        Enumerable
            .Range(1, dimension * dimension)
            .Select(value =>
                (BingoSquareDefinition)
                    new BingoSquareDefinition.Manual(new($"manual-{value}"), $"Manual {value}")
            )
            .ToArray();

    private static BingoGameDraft SharedGame(BingoTemplateId templateId) =>
        new(
            Guid.NewGuid(),
            templateId,
            BingoGameMode.Shared,
            "stable-seed",
            null,
            null,
            [],
            Actor()
        );

    private static BingoRosterCommand Roster(
        BingoGameView game,
        BingoViewer viewer,
        BingoTeamId? teamId
    ) => new(Guid.NewGuid(), game.Id, viewer, teamId, Actor(), "private roster reason");

    private static BingoGameActionCommand Action(BingoGameView game) =>
        new(Guid.NewGuid(), game.Id, Actor(), "private game reason");

    private static BingoManualMarkCommand Mark(
        BingoGameView game,
        BingoCardView card,
        int position,
        string note
    ) => new(Guid.NewGuid(), game.Id, card.Id, position, Actor(), note);

    private static BingoActor Actor() => new("moderator-id", "moderator");

    private static BingoViewer Viewer(string login) => new($"{login}-id", login, login);

    private static BingoOperationOutcome.Succeeded Success(BingoOperationOutcome result) =>
        result.ShouldBeOfType<BingoOperationOutcome.Succeeded>();

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = features,
            BingoAcceptEventsAfterUtc = _now.AddDays(-1).UtcDateTime,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SetFeatureAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features,
        DateTime acceptAfter
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = features;
        host.BingoAcceptEventsAfterUtc = acceptAfter;
        _ = await db.SaveChangesAsync();
    }

    private static async Task SeedExternalAchievementAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string key
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var season = new CommunitySeason
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            CreationOperationId = Guid.NewGuid(),
            Name = "Bingo rewards",
            Status = CommunitySeasonStatus.Open,
            Visibility = CommunityVisibility.Public,
            StartsAtUtc = _now.AddDays(-1).UtcDateTime,
            EndsAtUtc = _now.AddDays(30).UtcDateTime,
            Revision = 1,
            CreatedAtUtc = _now.UtcDateTime,
            UpdatedAtUtc = _now.UtcDateTime,
        };
        _ = db.CommunitySeasons.Add(season);
        _ = db.CommunityDefinitions.Add(
            new CommunityDefinition
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                Season = season,
                Key = key,
                Name = "Bingo winner",
                Kind = CommunityDefinitionKind.Achievement,
                Scope = CommunityProgressScope.Viewer,
                CompletionMode = CommunityCompletionMode.OneTime,
                EventRule = CommunityEventRuleKind.ExternalGrant,
                Increment = CommunityProgressIncrement.Occurrence,
                Target = 1,
                PointsReward = "0",
                ResetCadence = CommunityResetCadence.None,
                ResetLocalTime = "00:00",
                ScheduleRevision = 1,
                CreatedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private sealed class RecordingAchievementGrants : ICommunityAchievementGrantService
    {
        internal List<CommunityExternalGrantRequest> Requests { get; } = [];

        public Task<CommunityExternalGrantOutcome> GrantAsync(
            CommunityExternalGrantRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult<CommunityExternalGrantOutcome>(
                new CommunityExternalGrantOutcome.Granted(Guid.NewGuid(), false)
            );
        }
    }

    private sealed class RecordingReconciliationTrigger : IEventSubChannelReconciliationTrigger
    {
        internal int Calls { get; private set; }

        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class RecordingBingoOverlayEvents : IBingoOverlayEventObserver
    {
        internal List<BingoOverlayEvent> Events { get; } = [];

        public ValueTask BingoEventAsync(
            BingoOverlayEvent value,
            CancellationToken cancellationToken
        )
        {
            Events.Add(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailFirstIssuedGameSaveInterceptor : SaveChangesInterceptor
    {
        private int _remainingFailures = 1;

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        ) =>
            eventData
                .Context?.Set<BingoGame>()
                .Local.Any(value => value.Status == BingoGameStatus.Issued) == true
            && Interlocked.CompareExchange(ref _remainingFailures, 0, 1) == 1
                ? throw new InvalidOperationException("Simulated issue commit interruption.")
                : base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private abstract class SynchronizeFirstTwoSavesInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _arrived = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _remainingArrivals = 2;

        protected abstract bool IsTarget(BlokeBotDbContext context);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (eventData.Context is not BlokeBotDbContext db || !IsTarget(db))
            {
                return result;
            }
            var remaining = Interlocked.Decrement(ref _remainingArrivals);
            if (remaining == 0)
            {
                _arrived.SetResult();
            }
            if (remaining >= 0)
            {
                await _arrived.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class SynchronizeFirstGameCreatesInterceptor
        : SynchronizeFirstTwoSavesInterceptor
    {
        protected override bool IsTarget(BlokeBotDbContext context) =>
            context
                .ChangeTracker.Entries<BingoGame>()
                .Any(value => value.State == EntityState.Added);
    }

    private sealed class SynchronizeFirstParticipantJoinsInterceptor
        : SynchronizeFirstTwoSavesInterceptor
    {
        protected override bool IsTarget(BlokeBotDbContext context) =>
            context
                .ChangeTracker.Entries<BingoParticipant>()
                .Any(value => value.State == EntityState.Added);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
