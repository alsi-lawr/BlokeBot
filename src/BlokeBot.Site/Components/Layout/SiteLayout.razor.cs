using BlokeBot.Site.Content;

namespace BlokeBot.Site.Components.Layout;

public partial class SiteLayout
{
    private string? _activeSection
    {
        get
        {
            var relativeUri = Navigation.ToBaseRelativePath(Navigation.Uri);
            var route = $"/{relativeUri.Split('?', '#')[0].Trim('/')}";
            return route switch
            {
                "/how-it-works" => "overview",
                "/install" => "install",
                "/server-owners" => "server",
                "/guide" => "guide",
                _ => SiteRoutes.GuideTopics.Contains(route) ? "guide" : null,
            };
        }
    }

    private string? DockCurrent(string section) => _activeSection == section ? "page" : null;
}
