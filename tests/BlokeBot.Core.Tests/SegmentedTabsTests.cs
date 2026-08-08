using BlokeBot.Core.Components.Layout;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class SegmentedTabsTests
{
    private static readonly IReadOnlyList<SegmentedTabItem> _overlayItems =
    [
        new("sources", "Sources"),
        new("cues", "Cues"),
        new("media", "Media"),
    ];

    [Test]
    public void ActionTabs_ExposeActiveSemanticsAndSelectByKey()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        string? selected = null;
        var tabs = context.Render<SegmentedTabs>(parameters =>
            parameters
                .Add(value => value.AriaLabel, "Dashboard view")
                .Add(
                    value => value.Items,
                    [
                        new("live", "Live"),
                        new("history", "History"),
                        new("leaderboard", "Leaderboard"),
                    ]
                )
                .Add(value => value.ActiveKey, "history")
                .Add(
                    value => value.ActiveKeyChanged,
                    EventCallback.Factory.Create<string>(this, value => selected = value)
                )
        );

        tabs.Find("[role='tablist']").GetAttribute("aria-label").ShouldBe("Dashboard view");
        tabs.FindAll("[role='tab']").Count.ShouldBe(3);
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("History");
        tabs.FindAll("a").ShouldBeEmpty();

        tabs.FindAll("button").Single(button => button.TextContent.Trim() == "Leaderboard").Click();

        selected.ShouldBe("leaderboard");
    }

    [Test]
    public void RouteTabs_UseExactLinksAndFollowNavigation()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/reports/weekly");
        var tabs = context.Render<SegmentedTabs>(parameters =>
            parameters
                .Add(value => value.AriaLabel, "Report sections")
                .Add(
                    value => value.Items,
                    [
                        new("daily", "Daily", "reports/daily"),
                        new("weekly", "Weekly", "reports/weekly"),
                        new("monthly", "Monthly", "reports/monthly"),
                    ]
                )
        );

        tabs.FindAll("a")
            .Select(link => link.GetAttribute("href"))
            .ShouldBe(["reports/daily", "reports/weekly", "reports/monthly"]);
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Weekly");

        navigation.NavigateTo("/reports/monthly");

        tabs.WaitForAssertion(() =>
            tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Monthly")
        );
    }

    [Test]
    public void FragmentTabs_InitialFragmentSelectsTheMatchingTabWithoutNavigation()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays#cues");
        var historyDepth = navigation.History.Count;

        var tabs = RenderFragmentTabs(context);

        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Cues");
        navigation.Uri.ShouldEndWith("/overlays#cues");
        navigation.History.Count.ShouldBe(historyDepth);
    }

    [Test]
    [Arguments("/overlays")]
    [Arguments("/overlays#unknown-tab")]
    public void FragmentTabs_BareOrUnknownFragment_IsReplacedWithTheFirstCanonicalFragment(
        string initial
    )
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(initial);

        var tabs = RenderFragmentTabs(context);

        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Sources");
        navigation.Uri.ShouldEndWith("/overlays#sources");
        navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
    }

    [Test]
    public void FragmentTabs_SelectionPushesOneHistoryEntryAndBackForwardFollowTheFragment()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays#sources");
        var observed = new List<string>();

        var tabs = RenderFragmentTabs(context, observed.Add);

        tabs.FindAll("button").Single(button => button.TextContent.Trim() == "Cues").Click();

        navigation.Uri.ShouldEndWith("/overlays#cues");
        navigation.History.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Cues");
        observed.ShouldBe(["cues"]);

        navigation.NavigateTo("/overlays#sources");

        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Sources");
        observed.ShouldBe(["cues", "sources"]);

        navigation.NavigateTo("/overlays#cues");

        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Cues");
        observed.ShouldBe(["cues", "sources", "cues"]);
    }

    [Test]
    public void FragmentTabs_ReselectingTheActiveTab_NavigatesNowhere()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays#media");
        var tabs = RenderFragmentTabs(context);
        var historyDepth = navigation.History.Count;

        tabs.FindAll("button").Single(button => button.TextContent.Trim() == "Media").Click();

        navigation.History.Count.ShouldBe(historyDepth);
        navigation.Uri.ShouldEndWith("/overlays#media");
    }

    [Test]
    public void FragmentTabs_ExposeRovingFocusIdentityAndPanelOwnership()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays#cues");

        var tabs = RenderFragmentTabs(context);

        tabs.Find("#overlays-cues-tab").GetAttribute("aria-selected").ShouldBe("true");
        tabs.Find("#overlays-cues-tab").GetAttribute("tabindex").ShouldBe("0");
        tabs.Find("#overlays-cues-tab")
            .GetAttribute("aria-controls")
            .ShouldBe("overlays-cues-panel");
        tabs.Find("#overlays-sources-tab").GetAttribute("tabindex").ShouldBe("-1");
        tabs.Find("#overlays-media-tab").GetAttribute("tabindex").ShouldBe("-1");
        tabs.FindAll("[role='tab']").Select(tab => tab.GetAttribute("id")).ShouldBeUnique();
    }

    [Test]
    public void FragmentTabs_ArrowHomeAndEndKeys_SelectAndFocusTabs()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays#sources");

        var tabs = RenderFragmentTabs(context);

        tabs.Find("#overlays-sources-tab").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Cues");
        navigation.Uri.ShouldEndWith("/overlays#cues");

        tabs.Find("#overlays-cues-tab").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Sources");

        tabs.Find("#overlays-sources-tab").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Media");

        tabs.Find("#overlays-media-tab").KeyDown(new KeyboardEventArgs { Key = "Home" });
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Sources");

        tabs.Find("#overlays-sources-tab").KeyDown(new KeyboardEventArgs { Key = "End" });
        tabs.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Media");
        tabs.Find("#overlays-media-tab").GetAttribute("tabindex").ShouldBe("0");
    }

    [Test]
    public void FragmentTabs_LeavingTheOwnedPath_DoesNotRewriteTheNewLocation()
    {
        using var context = new BunitContext();
        _ = context.Services.AddScoped<DashboardFragmentState>();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/overlays#cues");
        _ = RenderFragmentTabs(context);

        navigation.NavigateTo("/guessing");

        navigation.Uri.ShouldEndWith("/guessing");
    }

    /// <summary>
    /// Custom commands owns the same fragment contract through its studio rail rather than a tab
    /// strip, so the assertion is that exactly one implementation of fragment ownership exists.
    /// </summary>
    [Test]
    public void FragmentOwningPages_ShareOneImplementationWithoutDuplicateTabMarkup()
    {
        var root = RepositoryRoot();
        var guessing = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BlokeBot.Core",
                "Features",
                "Guessing",
                "Rounds",
                "GuessingDashboard.razor"
            )
        );
        var overlays = File.ReadAllText(
            Path.Combine(root, "src", "BlokeBot.Core", "Features", "Overlays", "OverlaysPage.razor")
        );
        var customCommands = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BlokeBot.Core",
                "Features",
                "CustomCommands",
                "CustomCommandSettingsPage.razor"
            )
        );
        var customCommandsCode = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "BlokeBot.Core",
                "Features",
                "CustomCommands",
                "CustomCommandSettingsPage.razor.cs"
            )
        );

        guessing.ShouldContain("<SegmentedTabs");
        overlays.ShouldContain("<SegmentedTabs");
        guessing.ShouldContain("OwnsFragment");
        overlays.ShouldContain("OwnsFragment");
        guessing.ShouldNotContain("segmented-motion__indicator");
        overlays.ShouldNotContain("segmented-motion__indicator");
        customCommands.ShouldNotContain("segmented-motion__indicator");
        customCommands.ShouldNotContain("role=\"tablist\"");
        customCommands.ShouldContain("<StudioRail");
        customCommandsCode.ShouldContain("DashboardFragmentOwner");
        customCommandsCode.ShouldNotContain("UriPartial");
    }

    [Test]
    public void SharedPresentation_KeepsOneTabWidthOnDesktopAndDistributesAtPhoneWidth()
    {
        var styles = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "BlokeBot.Core",
                "Styles",
                "components",
                "segmented-controls.css"
            )
        );

        styles.ShouldContain(
            "grid-template-columns: repeat(var(--segmented-count), minmax(5.5rem, 7.5rem));"
        );
        styles.ShouldContain("justify-self: start;");
        styles.ShouldContain("width: max-content;");
        styles.ShouldContain(
            """
            .segmented-motion--shared .segmented-motion__tab {
                    align-items: center;
                    display: flex;
                    justify-content: center;
                    text-align: center;
                }
            """
        );
        styles.ShouldContain(
            "grid-template-columns: repeat(var(--segmented-count), minmax(0, 1fr));"
        );
        styles.ShouldContain("justify-self: stretch;");
        styles.ShouldContain("width: calc((100% - 0.5rem) / var(--segmented-count));");
        styles.ShouldContain("transform: translateX(calc(var(--segmented-active-index) * 100%));");
    }

    private static IRenderedComponent<FragmentTabsHost> RenderFragmentTabs(
        BunitContext context,
        Action<string>? onActiveKeyChanged = null
    ) =>
        context.Render<FragmentTabsHost>(parameters =>
            parameters
                .Add(value => value.Items, _overlayItems)
                .Add(value => value.OnChanged, onActiveKeyChanged)
        );

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>
    /// Mirrors the production parent contract: the page owns the selected key and passes it back
    /// into the fragment-owned tabs as <see cref="SegmentedTabs.ActiveKey"/>.
    /// </summary>
    private sealed class FragmentTabsHost : ComponentBase
    {
        private string _activeKey = string.Empty;

        [Inject]
        public NavigationManager Navigation { get; set; } = null!;

        [Parameter]
        public IReadOnlyList<SegmentedTabItem> Items { get; set; } = [];

        [Parameter]
        public Action<string>? OnChanged { get; set; }

        protected override void OnInitialized() =>
            _activeKey = SegmentedTabs.CanonicalKey(Navigation, Items);

        private void Handle(string key)
        {
            _activeKey = key;
            OnChanged?.Invoke(key);
        }

        protected override void BuildRenderTree(
            Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder
        )
        {
            builder.OpenComponent<SegmentedTabs>(0);
            builder.AddComponentParameter(1, nameof(SegmentedTabs.AriaLabel), "Overlays sections");
            builder.AddComponentParameter(2, nameof(SegmentedTabs.Items), Items);
            builder.AddComponentParameter(3, nameof(SegmentedTabs.Id), "overlays");
            builder.AddComponentParameter(4, nameof(SegmentedTabs.OwnsFragment), true);
            builder.AddComponentParameter(5, nameof(SegmentedTabs.ActiveKey), _activeKey);
            builder.AddComponentParameter(
                6,
                nameof(SegmentedTabs.ActiveKeyChanged),
                EventCallback.Factory.Create<string>(this, Handle)
            );
            builder.CloseComponent();
        }
    }
}
