using BlokeBot.Site.Content;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Site.Components;

public partial class GuideSidebar
{
    private string _currentTopicLabel
    {
        get
        {
            var relativeUri = _navigation.ToBaseRelativePath(_navigation.Uri);
            var relativePath = relativeUri.Split('?', '#')[0].Trim('/');
            return relativePath == "guide"
                ? "All help topics"
                : SiteGuideCatalog
                    .NavigationGroups.SelectMany(group => group.Links)
                    .FirstOrDefault(link => link.Href == relativePath)
                    ?.Label
                    ?? "All help topics";
        }
    }
}
