using System.Diagnostics;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bingo;

public partial class BingoPage
{
    private IReadOnlyList<BingoTemplateView> _templates = [];
    private IReadOnlyList<BingoModeratorGameView> _games = [];
    private IReadOnlyList<CounterChoice> _counters = [];
    private IReadOnlyList<string> _achievementKeys = [];
    private TemplateDraft _template = TemplateDraft.New(3);
    private GameDraft _game = new();
    private readonly Dictionary<string, string> _privateNotes = [];
    private readonly Dictionary<string, string> _teamMoves = [];
    private bool _featureEnabled;
    private bool _operationFailed;
    private string _feedback = string.Empty;

    private string _publicUrl => $"/bingo/{Uri.EscapeDataString(HostLogin)}";

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (HostId == 0)
        {
            return;
        }
        var features = await _features.Load(HostId).ExecuteAsync(CancellationToken.None);
        var configured = features.Match(
            option => option.Match(value => value, () => HostFeatureFlags.None),
            _ => throw new UnreachableException()
        );
        _featureEnabled = configured.Contains(HostFeatureFlags.Bingo);
        if (!_featureEnabled)
        {
            _templates = [];
            _games = [];
            return;
        }
        _templates = await _bingo.GetTemplatesAsync(HostId, CancellationToken.None);
        _games = await _bingo.GetModeratorGamesAsync(HostId, CancellationToken.None);
        await using var db = await _dbFactory.CreateDbContextAsync();
        _counters = await db
            .CustomCounters.AsNoTracking()
            .Where(value => value.HostId == HostId)
            .OrderBy(value => value.Name)
            .Select(value => new CounterChoice(value.Id, value.Name))
            .ToArrayAsync();
        _achievementKeys = await db
            .CommunityDefinitions.AsNoTracking()
            .Where(value =>
                value.HostId == HostId
                && value.Kind == CommunityDefinitionKind.Achievement
                && value.Scope == CommunityProgressScope.Viewer
                && value.EventRule == CommunityEventRuleKind.ExternalGrant
            )
            .OrderBy(value => value.Key)
            .Select(value => value.Key)
            .ToArrayAsync();
        if (_game.TemplateId == Guid.Empty && _templates.FirstOrDefault() is { } first)
        {
            _game.TemplateId = first.Id.Value;
        }
    }

    private async Task SaveTemplateAsync()
    {
        try
        {
            var linePoints = PointAmount.ParseAbsolute(_template.LinePoints);
            var fullPoints = PointAmount.ParseAbsolute(_template.FullPoints);
            await RunSelectedHostMutationAsync(
                HostId,
                async () =>
                {
                    var result = await _bingo.SaveTemplateAsync(
                        HostId,
                        new(
                            Guid.NewGuid(),
                            _template.Id is null ? null : new(_template.Id.Value),
                            _template.Name,
                            new(_template.Dimension),
                            _template.Squares.Select(ToDefinition).ToArray(),
                            _template.FullCard,
                            new(linePoints, Achievement(_template.LineAchievement)),
                            new(fullPoints, Achievement(_template.FullAchievement)),
                            Actor()
                        ),
                        CancellationToken.None
                    );
                    await CompleteAsync(result, "Template revision saved.");
                    if (result is BingoOperationOutcome.Succeeded)
                    {
                        _template = TemplateDraft.New(_template.Dimension);
                    }
                }
            );
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            Fail(exception.Message);
        }
    }

    private void EditTemplate(BingoTemplateView template) =>
        _template = new TemplateDraft
        {
            Id = template.Id.Value,
            Name = template.Name,
            Dimension = template.Dimension.Value,
            FullCard = template.FullCardWinEnabled,
            LinePoints = template.LineReward.Points.ToString(),
            LineAchievement = template.LineReward.AchievementKey?.Value ?? string.Empty,
            FullPoints = template.FullCardReward.Points.ToString(),
            FullAchievement = template.FullCardReward.AchievementKey?.Value ?? string.Empty,
            Squares = template.Squares.Select(SquareDraft.From).ToList(),
        };

    private void ChangeDimension(ChangeEventArgs args)
    {
        if (
            !int.TryParse(args.Value?.ToString(), out var dimension)
            || dimension is not (3 or 4 or 5)
        )
        {
            return;
        }
        _template.Resize(dimension);
    }

    private async Task CreateGameAsync()
    {
        var teams =
            _game.Mode == BingoGameMode.Team
                ? _game.Teams.Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                )
                : [];
        await RunMutationAsync(
            () =>
                _bingo.CreateGameAsync(
                    HostId,
                    new(
                        Guid.NewGuid(),
                        new(_game.TemplateId),
                        _game.Mode,
                        _game.Seed,
                        ParseCap(_game.ParticipantCap),
                        ParseCap(_game.TeamCap),
                        teams,
                        Actor()
                    ),
                    CancellationToken.None
                ),
            "Bingo game opened for joining."
        );
    }

    private Task IssueAsync(BingoGameView game) =>
        RunMutationAsync(
            () =>
                _bingo.IssueAsync(
                    HostId,
                    new(Guid.NewGuid(), game.Id, Actor(), Note($"game:{game.Id.Value:N}")),
                    CancellationToken.None
                ),
            "Cards issued. The roster and assignments are now frozen."
        );

    private Task ArchiveAsync(BingoGameView game) =>
        RunMutationAsync(
            () =>
                _bingo.ArchiveAsync(
                    HostId,
                    new(Guid.NewGuid(), game.Id, Actor(), Note($"game:{game.Id.Value:N}")),
                    CancellationToken.None
                ),
            "Bingo game archived."
        );

    private async Task MoveAsync(BingoGameView game, BingoViewer viewer)
    {
        var teamText = _teamMoves.GetValueOrDefault(viewer.TwitchUserId, string.Empty);
        var team = game.Teams.SingleOrDefault(value => value.Id.Value.ToString("N") == teamText);
        if (team is null)
        {
            Fail("Choose a team.");
            return;
        }
        await RunMutationAsync(
            () =>
                _bingo.MoveAsync(
                    HostId,
                    new(
                        Guid.NewGuid(),
                        game.Id,
                        viewer,
                        team.Id,
                        Actor(),
                        Note($"participant:{viewer.TwitchUserId}")
                    ),
                    CancellationToken.None
                ),
            $"Moved @{viewer.Login} to {team.Name}."
        );
    }

    private Task RemoveAsync(BingoGameView game, BingoViewer viewer) =>
        RunMutationAsync(
            () =>
                _bingo.RemoveAsync(
                    HostId,
                    new(
                        Guid.NewGuid(),
                        game.Id,
                        viewer,
                        null,
                        Actor(),
                        Note($"participant:{viewer.TwitchUserId}")
                    ),
                    CancellationToken.None
                ),
            $"Removed @{viewer.Login} from Bingo."
        );

    private Task ChangeMarkAsync(BingoGameView game, BingoCardView card, BingoSquareView square) =>
        RunMutationAsync(
            () =>
            {
                var command = new BingoManualMarkCommand(
                    Guid.NewGuid(),
                    game.Id,
                    card.Id,
                    square.Position,
                    Actor(),
                    Note($"mark:{card.Id.Value:N}:{square.Position}")
                );
                return square.Marked
                    ? _bingo.ReverseManualAsync(HostId, command, CancellationToken.None)
                    : _bingo.ConfirmManualAsync(HostId, command, CancellationToken.None);
            },
            square.Marked ? "Manual mark reversed." : "Manual square confirmed."
        );

    private Task RunMutationAsync(Func<Task<BingoOperationOutcome>> mutation, string success) =>
        RunSelectedHostMutationAsync(
            HostId,
            async () => await CompleteAsync(await mutation(), success)
        );

    private async Task CompleteAsync(BingoOperationOutcome result, string success)
    {
        _feedback = result switch
        {
            BingoOperationOutcome.Succeeded => success,
            BingoOperationOutcome.FeatureDisabled => "Bingo is off for this channel.",
            BingoOperationOutcome.NotFound => "That Bingo item was not found for this channel.",
            BingoOperationOutcome.Frozen => "The roster and cards are frozen.",
            BingoOperationOutcome.Conflict conflict => conflict.Message,
            BingoOperationOutcome.Invalid invalid => invalid.Message,
            _ => throw new UnreachableException(),
        };
        _operationFailed = result is not BingoOperationOutcome.Succeeded;
        await LoadAsync();
    }

    private BingoSquareDefinition ToDefinition(SquareDraft value)
    {
        var key = new BingoSquareKey(value.Key);
        return value.Kind switch
        {
            BingoSquareKind.Manual => new BingoSquareDefinition.Manual(
                key,
                value.Title,
                value.PrivateNote
            ),
            BingoSquareKind.IncomingRaid => new BingoSquareDefinition.IncomingRaid(
                key,
                value.Title,
                checked((int)value.Threshold),
                value.PrivateNote
            ),
            BingoSquareKind.BountyCompleted => new BingoSquareDefinition.BountyCompleted(
                key,
                value.Title,
                value.PrivateNote
            ),
            BingoSquareKind.GuessingResult => new BingoSquareDefinition.GuessingResult(
                key,
                value.Title,
                value.Filter,
                value.PrivateNote
            ),
            BingoSquareKind.GiveawayStarted => new BingoSquareDefinition.GiveawayStarted(
                key,
                value.Title,
                value.PrivateNote
            ),
            BingoSquareKind.StreamCategoryChanged =>
                new BingoSquareDefinition.StreamCategoryChanged(
                    key,
                    value.Title,
                    value.Filter,
                    value.PrivateNote
                ),
            BingoSquareKind.CounterReached => new BingoSquareDefinition.CounterReached(
                key,
                value.Title,
                value.CounterId,
                value.Threshold,
                value.PrivateNote
            ),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private BingoActor Actor() => new(PageContext.Session.UserId, PageContext.Session.Login);

    private string Note(string key) => _privateNotes.GetValueOrDefault(key, string.Empty);

    private void SetNote(string key, string value) => _privateNotes[key] = value;

    private static string ParticipantNoteKey(BingoViewer viewer) =>
        $"participant:{viewer.TwitchUserId}";

    private static string GameNoteKey(BingoGameView game) => $"game:{game.Id.Value:N}";

    private static string MarkNoteKey(BingoCardView card, BingoSquareView square) =>
        $"mark:{card.Id.Value:N}:{square.Position}";

    private static string TeamValue(BingoTeamView team) => team.Id.Value.ToString("N");

    private static CommunityDefinitionKey? Achievement(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : new(value);

    private static int? ParseCap(string value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private void Fail(string message)
    {
        _feedback = message;
        _operationFailed = true;
    }

    private sealed class TemplateDraft
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = "Stream moments";
        public int Dimension { get; set; }
        public bool FullCard { get; set; } = true;
        public string LinePoints { get; set; } = "0";
        public string LineAchievement { get; set; } = string.Empty;
        public string FullPoints { get; set; } = "0";
        public string FullAchievement { get; set; } = string.Empty;
        public List<SquareDraft> Squares { get; set; } = [];

        public static TemplateDraft New(int dimension)
        {
            var value = new TemplateDraft { Dimension = dimension };
            value.Resize(dimension);
            return value;
        }

        public void Resize(int dimension)
        {
            Dimension = dimension;
            var count = dimension * dimension;
            while (Squares.Count < count)
            {
                Squares.Add(SquareDraft.New(Squares.Count));
            }
            if (Squares.Count > count)
            {
                Squares.RemoveRange(count, Squares.Count - count);
            }
        }
    }

    private sealed record SquareDraft
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public BingoSquareKind Kind { get; set; }
        public long Threshold { get; set; } = 1;
        public string Filter { get; set; } = string.Empty;
        public int CounterId { get; set; }
        public string PrivateNote { get; set; } = string.Empty;

        public static SquareDraft New(int index) =>
            new() { Key = $"square-{index + 1}", Title = $"Moment {index + 1}" };

        public static SquareDraft From(BingoSquareDefinition value) =>
            value switch
            {
                BingoSquareDefinition.Manual manual => Base(manual),
                BingoSquareDefinition.IncomingRaid raid => Base(raid) with
                {
                    Threshold = raid.MinimumViewerCount,
                },
                BingoSquareDefinition.BountyCompleted bounty => Base(bounty),
                BingoSquareDefinition.GuessingResult guessing => Base(guessing) with
                {
                    Filter = guessing.WinningAnswer ?? string.Empty,
                },
                BingoSquareDefinition.GiveawayStarted giveaway => Base(giveaway),
                BingoSquareDefinition.StreamCategoryChanged category => Base(category) with
                {
                    Filter = category.CategoryId ?? string.Empty,
                },
                BingoSquareDefinition.CounterReached counter => Base(counter) with
                {
                    CounterId = counter.CounterId,
                    Threshold = counter.Target,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };

        private static SquareDraft Base(BingoSquareDefinition value) =>
            new()
            {
                Key = value.Key.Value,
                Title = value.Title,
                Kind = value.Kind,
                PrivateNote = value.PrivateModeratorNote,
            };
    }

    private sealed class GameDraft
    {
        public Guid TemplateId { get; set; }
        public BingoGameMode Mode { get; set; }
        public string Seed { get; set; } =
            DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        public string ParticipantCap { get; set; } = string.Empty;
        public string TeamCap { get; set; } = string.Empty;
        public string Teams { get; set; } = "Team Aurora, Team Nebula";
    }

    private sealed record CounterChoice(int Id, string Name);
}
