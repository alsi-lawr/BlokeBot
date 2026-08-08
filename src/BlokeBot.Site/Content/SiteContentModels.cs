namespace BlokeBot.Site.Content;

internal sealed record SiteLink(string Label, string Href);

internal sealed record SiteGuideNavigationGroup(string Label, IReadOnlyList<SiteLink> Links);

internal sealed record SiteMedia(
    string DarkPhoneSource,
    string LightPhoneSource,
    string DarkLaptopSource,
    string LightLaptopSource,
    string PhoneAlt,
    string LaptopAlt,
    string Caption
);

internal sealed record SiteGuideSection
{
    internal required string Heading { get; init; }

    internal string? Anchor { get; init; }

    internal SiteMedia? Media { get; init; }

    internal IReadOnlyList<string> Paragraphs { get; init; } = [];

    internal IReadOnlyList<string> Steps { get; init; } = [];

    internal IReadOnlyList<string> Bullets { get; init; } = [];

    internal IReadOnlyList<SiteLink> Links { get; init; } = [];

    internal string? Code { get; init; }

    internal string? Note { get; init; }
}

internal sealed record SiteGuidePage
{
    internal required string Route { get; init; }

    internal string Href => Route.TrimStart('/');

    internal required string Eyebrow { get; init; }

    internal required string Title { get; init; }

    internal required string Summary { get; init; }

    internal IReadOnlyList<SiteGuideSection> Sections { get; init; } = [];

    internal SiteMedia? Media { get; init; }

    internal IReadOnlyList<SiteLink> Next { get; init; } = [];
}
