using BlokeBot.Auth.Web;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class LoginPageTests
{
    [Test]
    public void NormalPage_Rendering_PreservesLoginAndLeaderboardMarkupWithoutError()
    {
        var page = LoginPage.Render();

        page.ShouldContain("<!DOCTYPE html>");
        page.ShouldContain("<title>Sign in to BlokeBot</title>");
        page.ShouldContain("href=\"/app.css\"");
        page.ShouldContain("href=\"/auth/login?start=true\"");
        page.ShouldContain("data-public-leaderboard-form");
        page.ShouldContain("name=\"feature\"");
        page.ShouldContain("name=\"channel\"");
        page.ShouldNotContain("border-rose-200");
    }

    [Test]
    public void ErrorPage_Rendering_EncodesErrorAndPreservesLoginMarkup()
    {
        const string ErrorMarkup = "<script>alert(1)</script>&";

        var page = LoginPage.RenderError(ErrorMarkup);

        page.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;&amp;");
        page.ShouldNotContain(ErrorMarkup);
        page.ShouldContain("border-rose-200");
        page.ShouldContain("href=\"/auth/login?start=true\"");
    }

    [Test]
    public void InvalidError_Rendering_RejectsNullEmptyAndWhitespace()
    {
        Should.Throw<ArgumentException>(() => LoginPage.RenderError(null!));
        Should.Throw<ArgumentException>(() => LoginPage.RenderError(string.Empty));
        Should.Throw<ArgumentException>(() => LoginPage.RenderError(" \t\r\n"));
    }

    [Test]
    public void NormalPage_Rendering_PreservesThemePreferenceFallbackScript()
    {
        var page = LoginPage.Render();

        page.ShouldContain("const storageKey = \"blokebot.theme\";");
        page.ShouldContain("window.matchMedia(\"(prefers-color-scheme: dark)\")");
        page.ShouldContain("return valid(value) ? value : null;");
        page.ShouldContain("return null;");
        page.ShouldContain("storedTheme() ?? systemTheme()");
        page.ShouldContain("document.documentElement.dataset.theme = theme;");
        page.ShouldContain("document.documentElement.style.colorScheme = theme;");
    }
}
