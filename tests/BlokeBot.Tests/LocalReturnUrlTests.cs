using BlokeBot.Auth.Web;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class LocalReturnUrlTests
{
    [Test]
    public void Return_url_keeps_local_app_paths()
    {
        LocalReturnUrl.OrFallback("/", "/fallback").ShouldBe("/");
        LocalReturnUrl.OrFallback("/guessing", "/fallback").ShouldBe("/guessing");
        LocalReturnUrl.OrFallback("/host/create?tab=auth#bot", "/fallback")
            .ShouldBe("/host/create?tab=auth#bot");
    }

    [Test]
    public void Return_url_rejects_protocol_relative_and_absolute_urls()
    {
        LocalReturnUrl.OrFallback("//attacker.invalid", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("https://attacker.invalid/", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("http://attacker.invalid/", "/fallback").ShouldBe("/fallback");
    }

    [Test]
    public void Return_url_rejects_backslash_variants()
    {
        LocalReturnUrl.OrFallback("\\\\attacker.invalid", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("/\\attacker.invalid", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("/%5cattacker.invalid", "/fallback").ShouldBe("/fallback");
    }

    [Test]
    public void Return_url_rejects_empty_or_relative_values()
    {
        LocalReturnUrl.OrFallback(null, "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("", "/fallback").ShouldBe("/fallback");
        LocalReturnUrl.OrFallback("relative/path", "/fallback").ShouldBe("/fallback");
    }
}
