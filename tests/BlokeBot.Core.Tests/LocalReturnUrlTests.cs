using BlokeBot.Core.Auth.Web;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class LocalReturnUrlTests
{
    [Test]
    public void LocalAppPath_NormalizingReturnUrl_PreservesPath()
    {
        LocalReturnUrl.OrFallback("/", "/fallback").ShouldBe("/");
        LocalReturnUrl.OrFallback("/guessing", "/fallback").ShouldBe("/guessing");
        LocalReturnUrl
            .OrFallback("/host/create?tab=auth#bot", "/fallback")
            .ShouldBe("/host/create?tab=auth#bot");
    }

    [Test]
    public void AbsoluteOrProtocolRelativeUrl_NormalizingReturnUrl_UsesFallback()
    {
        LocalReturnUrl.OrFallback("//attacker.invalid", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("https://attacker.invalid/", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("http://attacker.invalid/", "/fallback").ShouldBe("/fallback");
    }

    [Test]
    public void BackslashUrlVariant_NormalizingReturnUrl_UsesFallback()
    {
        LocalReturnUrl.OrFallback("\\\\attacker.invalid", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("/\\attacker.invalid", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("/%5cattacker.invalid", "/fallback").ShouldBe("/fallback");
    }

    [Test]
    public void EmptyOrRelativeUrl_NormalizingReturnUrl_UsesFallback()
    {
        LocalReturnUrl.OrFallback(null, "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("relative/path", "/fallback").ShouldBe("/fallback");
    }
}
