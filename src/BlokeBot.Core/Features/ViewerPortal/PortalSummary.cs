using System.Collections.Immutable;

namespace BlokeBot.Core.Features.ViewerPortal;

public sealed record PortalLink(string Label, string Href);

public sealed record PortalActivity(DateTime OccurredAtUtc, string Description, PortalLink Link);

public sealed record PortalSummary(
    string Headline,
    string Detail,
    bool IsActive,
    ImmutableArray<PortalLink> Links,
    ImmutableArray<PortalActivity> RecentActivity
);

internal static class PortalSummaryBounds
{
    internal const int Items = 5;
    internal const int TextLength = 160;

    internal static string Text(string value) =>
        value.Length <= TextLength ? value : value[..TextLength];

    internal static PortalLink Link(string label, string href) => new(Text(label), href);

    internal static PortalSummary Create(
        string headline,
        string detail,
        bool active,
        IEnumerable<PortalLink> links,
        IEnumerable<PortalActivity>? activity = null
    ) =>
        new(
            Text(headline),
            Text(detail),
            active,
            links.Take(Items).ToImmutableArray(),
            Merge(activity ?? [])
        );

    internal static ImmutableArray<PortalActivity> Merge(IEnumerable<PortalActivity> activity) =>
        activity
            .OrderByDescending(value => value.OccurredAtUtc)
            .ThenBy(value => value.Link.Href, StringComparer.Ordinal)
            .ThenBy(value => value.Description, StringComparer.Ordinal)
            .Take(Items)
            .Select(value => value with { Description = Text(value.Description) })
            .ToImmutableArray();
}
