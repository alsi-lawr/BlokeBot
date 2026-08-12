using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace BlokeBot.Core.Tests;

/// <summary>
/// The cross-ticket inventory required by BLOKEBOT-208: every same-route workspace tab strip in the
/// dashboard must be backed by a URL fragment, either by letting the shared strip own the fragment
/// or by a page that drives it through <c>DashboardFragmentOwner</c>.
/// </summary>
public sealed partial class WorkspaceTabConsumerInventoryTests
{
    [Test]
    public void IntegratedConsumers_DriveTheirWorkspaceTabsFromAFragment()
    {
        var integrated = new[]
        {
            "Overlays/OverlaysPage.razor",
            "Guessing/Rounds/GuessingDashboard.razor",
            "CustomCommands/CustomCommandSettingsPage.razor",
            "PlayWithViewers/PlayQueuesPage.razor",
            "RequestBoards/RequestBoardsPage.razor",
        };

        foreach (var relative in integrated)
        {
            var file = Path.Combine(DashboardSourceRoot(), relative);
            File.Exists(file).ShouldBeTrue(file);
            IsFragmentBacked(file).ShouldBeTrue(relative);
        }
    }

    [Test]
    [Skip(
        "Pending BLOKEBOT-202, -203, -206 and -209. Competitions still renders a workspace tab "
            + "strip with no fragment, and the remaining local adoptions land with those tickets. "
            + "The last of them removes this Skip."
    )]
    public void EveryWorkspaceTabStrip_IsFragmentBacked()
    {
        var offenders = Directory
            .EnumerateFiles(DashboardSourceRoot(), "*.razor", SearchOption.AllDirectories)
            .Where(static file =>
                File.ReadAllText(file).Contains("<SegmentedTabs", StringComparison.Ordinal)
            )
            .Where(static file => !IsFragmentBacked(file))
            .Select(file => Path.GetRelativePath(DashboardSourceRoot(), file))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    private static bool IsFragmentBacked(string razorFile)
    {
        var markup = File.ReadAllText(razorFile);
        if (OwnsFragmentUsage().IsMatch(markup))
        {
            return true;
        }

        var codeBehind = razorFile + ".cs";
        return File.Exists(codeBehind)
            && File.ReadAllText(codeBehind)
                .Contains("DashboardFragmentOwner", StringComparison.Ordinal);
    }

    private static string DashboardSourceRoot([CallerFilePath] string testFile = "") =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(testFile)!,
                "..",
                "..",
                "src",
                "BlokeBot.Core",
                "Features"
            )
        );

    [GeneratedRegex(@"<SegmentedTabs\b[^>]*\bOwnsFragment\b", RegexOptions.Singleline)]
    private static partial Regex OwnsFragmentUsage();
}
