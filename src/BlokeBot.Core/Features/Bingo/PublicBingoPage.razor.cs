using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Bingo;

public partial class PublicBingoPage
{
    private const string _archiveKey = "archive";

    /// <summary>Sections default to open, so only what a reader has closed is tracked.</summary>
    private readonly HashSet<string> _closed = [];
    private BingoPublicView? _view;
    private string? _loadedChannel;
    private bool _loaded;

    [Parameter]
    public string Channel { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        if (!string.Equals(_loadedChannel, Channel, StringComparison.OrdinalIgnoreCase))
        {
            _closed.Clear();
            _loadedChannel = Channel;
            _loaded = false;
        }

        _view = await _bingo.GetPublicAsync(Channel, CancellationToken.None);
        _loaded = true;
    }

    private bool IsOpen(string key) => !_closed.Contains(key);

    private void SetOpen(string key, bool open) =>
        _ = open ? _closed.Remove(key) : _closed.Add(key);

    private static string ModeDescription(BingoGameView game) =>
        game.Mode switch
        {
            BingoGameMode.Shared => "Shared board. Everyone plays the same card.",
            BingoGameMode.UniquePerViewer => "Every viewer plays their own dealt card.",
            _ => "Teams play one card each.",
        };

    private static string CardCount(BingoGameView game) =>
        game.Cards.Count == 1 ? "1 card" : $"{game.Cards.Count} cards";

    private static string ArchiveSummary(BingoGameView game)
    {
        var wins = game.WinCount();
        var date = (game.ArchivedAtUtc ?? game.CompletedAtUtc ?? game.CreatedAtUtc).DayStamp();
        var winText = wins == 1 ? "1 win" : $"{wins} wins";
        return $"{game.Dimension.GridLabel()} · {game.Mode.Label()} · {winText} · {date}";
    }
}
