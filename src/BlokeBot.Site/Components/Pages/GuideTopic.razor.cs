using BlokeBot.Site.Content;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Site.Components.Pages;

public partial class GuideTopic
{
    private SiteGuidePage _page = SiteGuideCatalog.Get("/guide/getting-started");

    protected override void OnParametersSet()
    {
        var relativeUri = Navigation.ToBaseRelativePath(Navigation.Uri);
        var relativePath = relativeUri.Split('?', '#')[0];
        _page = SiteGuideCatalog.Get($"/{relativePath.TrimStart('/')}");
    }
}
