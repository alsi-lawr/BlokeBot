using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

public abstract record OverlayEventPresentation
{
    private OverlayEventPresentation() { }

    public abstract OverlayEventFeedKind Kind { get; }
    public required int HostId { get; init; }
    public required string SourceKey { get; init; }

    public sealed record PointAward : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.PointAward;
        public required string Recipient { get; init; }
        public required string Amount { get; init; }
        public required string PointLabel { get; init; }
    }

    public sealed record GuessingWinner : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.GuessingWinner;
        public required string RoundName { get; init; }
        public required string WinningAnswer { get; init; }
        public ImmutableArray<string> Winners { get; init; } = [];
        public required string Amount { get; init; }
        public required string PointLabel { get; init; }
    }

    public sealed record GiveawayWinner : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.GiveawayWinner;
        public ImmutableArray<string> Winners { get; init; } = [];
        public ImmutableArray<string> Prizes { get; init; } = [];
        public required string PointLabel { get; init; }
    }

    public sealed record BingoEvent : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.BingoEvent;
        public required string Summary { get; init; }
    }

    public sealed record AchievementCompletion : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.AchievementCompletion;
        public required string Viewer { get; init; }
        public required string Achievement { get; init; }
        public required string Rewards { get; init; }
    }
}

public interface IOverlayEventPresenter
{
    Task PresentAsync(OverlayEventPresentation presentation, CancellationToken cancellationToken);
}

public sealed record EventFeedCardPresentation(
    long Id,
    string Kind,
    string Priority,
    string Title,
    string Body,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? DisplayDeadlineUtc
);

public sealed record EventFeedStatePresentation(
    EventFeedCardPresentation? Active,
    IReadOnlyList<EventFeedCardPresentation> Pending
);

internal sealed partial class EventFeedTemplateRenderer
{
    [GeneratedRegex("\\{([A-Za-z][A-Za-z0-9]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    internal static string Render(
        EventFeedKindConfiguration configuration,
        OverlayEventPresentation presentation
    )
    {
        var values = Values(presentation);
        ValidateTemplate(configuration.Template, presentation.Kind, values);
        var rendered = PlaceholderPattern()
            .Replace(
                configuration.Template,
                match =>
                {
                    var name = match.Groups[1].Value;
                    return values[name];
                }
            );
        return HtmlEncoder.Default.Encode(rendered);
    }

    private static void ValidateTemplate(
        string template,
        OverlayEventFeedKind kind,
        IReadOnlyDictionary<string, string> values
    )
    {
        var withoutKnownPlaceholders = PlaceholderPattern()
            .Replace(
                template,
                match =>
                {
                    var name = match.Groups[1].Value;
                    return !values.ContainsKey(name)
                        ? throw new ArgumentException(
                            $"Placeholder {{{name}}} is not valid for {kind}."
                        )
                        : string.Empty;
                }
            );
        if (
            withoutKnownPlaceholders.Contains('{', StringComparison.Ordinal)
            || withoutKnownPlaceholders.Contains('}', StringComparison.Ordinal)
        )
        {
            throw new ArgumentException("Templates may contain only known placeholders.");
        }
    }

    private static IReadOnlyDictionary<string, string> Values(
        OverlayEventPresentation presentation
    ) =>
        presentation switch
        {
            OverlayEventPresentation.PointAward point => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["recipient"] = point.Recipient,
                ["amount"] = point.Amount,
                ["pointLabel"] = point.PointLabel,
            },
            OverlayEventPresentation.GuessingWinner guessing => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["roundName"] = guessing.RoundName,
                ["winningAnswer"] = guessing.WinningAnswer,
                ["winners"] = Join(guessing.Winners),
                ["winnerCount"] = guessing.Winners.Length.ToString(CultureInfo.InvariantCulture),
                ["amount"] = guessing.Amount,
                ["pointLabel"] = guessing.PointLabel,
            },
            OverlayEventPresentation.GiveawayWinner giveaway => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["winners"] = Join(giveaway.Winners),
                ["winnerCount"] = giveaway.Winners.Length.ToString(CultureInfo.InvariantCulture),
                ["prizes"] = Join(giveaway.Prizes),
                ["pointLabel"] = giveaway.PointLabel,
            },
            OverlayEventPresentation.BingoEvent bingo => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["summary"] = bingo.Summary,
            },
            OverlayEventPresentation.AchievementCompletion achievement => new Dictionary<
                string,
                string
            >(StringComparer.Ordinal)
            {
                ["viewer"] = achievement.Viewer,
                ["achievement"] = achievement.Achievement,
                ["rewards"] = achievement.Rewards,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(presentation)),
        };

    private static string Join(IEnumerable<string> values) => string.Join(", ", values);
}

internal static class EventFeedProjectionText
{
    internal static string DecodeOnce(string durableText) => WebUtility.HtmlDecode(durableText);
}
