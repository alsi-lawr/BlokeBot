using BlokeBot.Site.Content;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Site.Components;

public partial class GuideSidebar
{
    private string _currentRelativePath
    {
        get
        {
            var relativeUri = _navigation.ToBaseRelativePath(_navigation.Uri);
            return relativeUri.Split('?', '#')[0].Trim('/');
        }
    }

    private string _currentTopicLabel =>
        _currentRelativePath == "guide"
            ? "All help topics"
            : SiteGuideCatalog
                .NavigationGroups.SelectMany(group => group.Links)
                .FirstOrDefault(link => LinkPath(link) == _currentRelativePath)
                ?.Label
                ?? "All help topics";

    private bool IsCurrentGroup(SiteGuideNavigationGroup group) =>
        group.Links.Any(link => LinkPath(link) == _currentRelativePath);

    private static string LinkPath(SiteLink link) => link.Href.Split('?', '#')[0].Trim('/');
}
