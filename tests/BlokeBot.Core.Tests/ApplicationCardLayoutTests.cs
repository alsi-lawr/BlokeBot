using System.Globalization;
using System.Text.RegularExpressions;
using BlokeBot.Core.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ApplicationCardLayoutTests
{
    private const int _baselineCardAuthoringSiteCount = 98;
    private const int _baselineDisclosureAuthoringSiteCount = 40;

    private static readonly Regex _cardClassToken = new(
        """(?<![-\w])card(?![-\w])""",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex _cardOwner = new(
        "(?:data-card-owner|Owner)=\"(?<owners>[^\"]+)\"",
        RegexOptions.CultureInvariant
    );

    [Test]
    public void MembershipMapClassifiesEveryCardAndDisclosureAuthoringSite()
    {
        var repositoryRoot = RepositoryRoot();
        var classifications = ReadMembershipMap(repositoryRoot);
        var authoredSites = FindAuthoredSites(repositoryRoot);

        classifications
            .Count(entry => entry.Kind is CardAuthoringKind.Card)
            .ShouldBe(_baselineCardAuthoringSiteCount);
        classifications
            .Count(entry => entry.Kind is CardAuthoringKind.Disclosure)
            .ShouldBe(_baselineDisclosureAuthoringSiteCount);
        authoredSites
            .Count(site => site.Kind is CardAuthoringKind.Card)
            .ShouldBe(_baselineCardAuthoringSiteCount);
        authoredSites
            .Count(site => site.Kind is CardAuthoringKind.Disclosure)
            .ShouldBe(_baselineDisclosureAuthoringSiteCount);

        classifications.Select(entry => entry.Location).ShouldBeUnique();
        classifications.ShouldAllBe(entry => entry.OwningCollection.Length > 0);
        classifications
            .Select(entry => entry.Location)
            .Select(LocationKey)
            .Order()
            .ShouldBe(authoredSites.Select(LocationKey).Order());

        classifications
            .Where(entry => entry.Kind is CardAuthoringKind.Card)
            .ShouldAllBe(entry => entry.Membership == CardMembership.Included);
        classifications
            .Count(entry =>
                entry.Kind is CardAuthoringKind.Disclosure
                && entry.Membership is CardMembership.Excluded
            )
            .ShouldBe(5);

        var concreteOwners = FindConcreteOwners(repositoryRoot);
        classifications
            .Where(entry => entry.Membership is CardMembership.Included)
            .Select(entry => entry.OwningCollection)
            .Distinct()
            .ShouldAllBe(owner => concreteOwners.Contains(owner));
    }

    [Test]
    public void UnclassifiedCardAndDisclosureFixtureIsRejected()
    {
        var classifiedLocations = ReadMembershipMap(RepositoryRoot())
            .Select(entry => entry.Location)
            .ToHashSet();
        const string FixturePath = "fixture/UnclassifiedCard.razor";
        var fixture = new[]
        {
            """<section class="card">Unclassified card</section>""",
            """<CollapsibleSection Title="Unclassified disclosure">Content</CollapsibleSection>""",
        };

        var unclassified = FindAuthoredSites(FixturePath, fixture)
            .Where(location => !classifiedLocations.Contains(location))
            .ToArray();

        unclassified.Length.ShouldBe(2);
        unclassified.ShouldContain(
            new CardAuthoringLocation(CardAuthoringKind.Card, FixturePath, 1)
        );
        unclassified.ShouldContain(
            new CardAuthoringLocation(CardAuthoringKind.Disclosure, FixturePath, 2)
        );
    }

    [Test]
    public void SharedCollectionCompositionOwnsTheTwelvePixelClearance()
    {
        var repositoryRoot = RepositoryRoot();
        var styles = ReadRepositoryFile(
            repositoryRoot,
            "src",
            "BlokeBot.Core",
            "Styles",
            "components",
            "application-card-layout.css"
        );
        var normalizedStyles = Normalize(styles);

        normalizedStyles.ShouldContain(":root { --app-card-clearance: 12px; }");
        normalizedStyles.ShouldContain(
            ".application-card-collection, .settings-disclosure-stack { display: grid; gap: var(--app-card-clearance); }"
        );

        var applicationStyles = ReadRepositoryFile(
            repositoryRoot,
            "src",
            "BlokeBot.Core",
            "Styles",
            "app.css"
        );
        applicationStyles.ShouldContain("""@import "./components/application-card-layout.css";""");

        var pageContextStyles = ReadRepositoryFile(
            repositoryRoot,
            "src",
            "BlokeBot.Core",
            "Styles",
            "components",
            "page-context.css"
        );
        pageContextStyles.ShouldNotContain(".settings-disclosure-stack {");

        var nativeStyles = ReadRepositoryFile(
            repositoryRoot,
            "src",
            "BlokeBot.Core",
            "Styles",
            "features",
            "native-twitch.css"
        );
        nativeStyles.ShouldNotContain(".dashboard-page[data-native-route] {");
    }

    [Test]
    public void ExcludedPageAndTaskPanelsKeepTheEstablishedPageRhythm()
    {
        using var context = new BunitContext();
        RenderFragment content = builder => builder.AddMarkupContent(0, "<p>Ready</p>");
        var dashboard = context.Render<DashboardPage>(parameters =>
            parameters
                .Add(parameter => parameter.Title, "Cards")
                .Add(parameter => parameter.ChildContent, content)
        );

        dashboard.Find(".dashboard-page").ClassList.ShouldNotContain("application-card-collection");

        var loadingDashboard = context.Render<DashboardPage>(parameters =>
            parameters
                .Add(parameter => parameter.Title, "Loading")
                .Add(parameter => parameter.LoadState, new PageLoadState.Loading("Loading cards"))
                .Add(parameter => parameter.ChildContent, content)
        );
        loadingDashboard
            .Find(".dashboard-page")
            .ClassList.ShouldNotContain("application-card-collection");
        loadingDashboard
            .Find(".page-state")
            .ClassList.ShouldNotContain("application-card-collection");

        var taskPanel = context.Render<TaskPanel>(parameters =>
            parameters
                .Add(parameter => parameter.Title, "Task")
                .Add(parameter => parameter.ChildContent, content)
        );
        taskPanel.Find(".task-panel").ClassList.ShouldNotContain("application-card-collection");

        var pageState = context.Render<PageState>(parameters =>
            parameters
                .Add(parameter => parameter.Kind, PageStateKind.Empty)
                .Add(parameter => parameter.Title, "Empty")
        );
        pageState.Find(".page-state").ClassList.ShouldNotContain("application-card-collection");

        var repositoryRoot = RepositoryRoot();
        var pageStyles = Normalize(
            ReadRepositoryFile(
                repositoryRoot,
                "src",
                "BlokeBot.Core",
                "Styles",
                "components",
                "page-context.css"
            )
        );
        pageStyles.ShouldContain(
            ".dashboard-page { display: grid; gap: 1.5rem; margin-inline: auto; width: 100%; }"
        );
    }

    [Test]
    public void HomeAndFeatureCompositionsUseTheApplicationDefault()
    {
        var repositoryRoot = RepositoryRoot();
        var home = ReadRepositoryFile(
            repositoryRoot,
            "src",
            "BlokeBot.Core",
            "Features",
            "Home",
            "HomePage.razor"
        );
        home.ShouldContain(
            """<section class="application-card-collection md:grid-cols-2" data-card-owner="home-card-grid">"""
        );
        home.ShouldContain("home-note rounded-lg p-5");

        var channelSetup = ReadRepositoryFile(
            repositoryRoot,
            "src",
            "BlokeBot.Core",
            "Features",
            "HostConfig",
            "Page",
            "HostConfigPage.razor"
        );
        channelSetup.ShouldContain(
            """<div class="application-card-collection p-3 sm:grid-cols-2 xl:grid-cols-3" data-card-owner="channel-setup-feature-cards">"""
        );
    }

    private static IReadOnlySet<string> FindConcreteOwners(string repositoryRoot)
    {
        var coreRoot = Path.Combine(repositoryRoot, "src", "BlokeBot.Core");
        return Directory
            .EnumerateFiles(coreRoot, "*.razor", SearchOption.AllDirectories)
            .SelectMany(static path =>
                _cardOwner
                    .Matches(File.ReadAllText(path))
                    .SelectMany(static match =>
                        match
                            .Groups["owners"]
                            .Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    )
            )
            .Where(static owner => owner[0] != '@')
            .ToHashSet();
    }

    private static IReadOnlyList<CardAuthoringLocation> FindAuthoredSites(string repositoryRoot)
    {
        var coreRoot = Path.Combine(repositoryRoot, "src", "BlokeBot.Core");
        return Directory
            .EnumerateFiles(coreRoot, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(coreRoot, "*.razor.cs", SearchOption.AllDirectories))
            .SelectMany(path =>
                FindAuthoredSites(
                    Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    File.ReadLines(path)
                )
            )
            .ToArray();
    }

    private static IEnumerable<CardAuthoringLocation> FindAuthoredSites(
        string relativePath,
        IEnumerable<string> lines
    )
    {
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            if (_cardClassToken.IsMatch(line))
            {
                yield return new(CardAuthoringKind.Card, relativePath, lineNumber);
            }

            if (line.Contains("<CollapsibleSection", StringComparison.Ordinal))
            {
                yield return new(CardAuthoringKind.Disclosure, relativePath, lineNumber);
            }
        }
    }

    private static IReadOnlyList<CardMembershipEntry> ReadMembershipMap(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            "tests",
            "BlokeBot.Core.Tests",
            "ApplicationCardMembership.tsv"
        );
        return File.ReadLines(path)
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(ParseMembership)
            .ToArray();
    }

    private static CardMembershipEntry ParseMembership(string line)
    {
        var columns = line.Split('|');
        columns.Length.ShouldBe(5);
        return new(
            Enum.Parse<CardAuthoringKind>(columns[0], ignoreCase: true),
            columns[1],
            int.Parse(columns[2], CultureInfo.InvariantCulture),
            Enum.Parse<CardMembership>(columns[3], ignoreCase: true),
            columns[4]
        );
    }

    private static string RepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the BlokeBot repository root.");
    }

    private static string ReadRepositoryFile(string repositoryRoot, params string[] relativePath) =>
        File.ReadAllText(Path.Combine([repositoryRoot, .. relativePath]));

    private static string Normalize(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string LocationKey(CardAuthoringLocation location) =>
        $"{location.Kind}|{location.SourcePath}|{location.Line}";

    private enum CardAuthoringKind
    {
        Card,
        Disclosure,
    }

    private enum CardMembership
    {
        Included,
        Excluded,
    }

    private sealed record CardAuthoringLocation(
        CardAuthoringKind Kind,
        string SourcePath,
        int Line
    );

    private sealed record CardMembershipEntry(
        CardAuthoringKind Kind,
        string SourcePath,
        int Line,
        CardMembership Membership,
        string OwningCollection
    )
    {
        public CardAuthoringLocation Location => new(Kind, SourcePath, Line);
    }
}
