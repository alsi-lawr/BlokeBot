using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed partial class PluginGuideNavigationTests
{
    [Test]
    public async Task PluginGuides_DirectRoutesAndRenderedLocalTargets_AreServed()
    {
        await using var app = SiteApplication.Build([
            "--urls=http://127.0.0.1:0",
            .. SiteTestConfiguration.PrivacyArguments,
        ]);

        try
        {
            await app.StartAsync();
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var targets = new HashSet<string>(StringComparer.Ordinal);

            foreach (var route in new[] { "/plugins", "/server-owners/plugins" })
            {
                using var response = await client.GetAsync(route);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                var html = await response.Content.ReadAsStringAsync();
                foreach (Match match in LocalTarget().Matches(html))
                {
                    var target = WebUtility.HtmlDecode(match.Groups[1].Value);
                    if (Local(target))
                    {
                        _ = targets.Add(target);
                    }
                }
            }

            foreach (var target in targets)
            {
                using var response = await client.GetAsync(target);
                response.IsSuccessStatusCode.ShouldBeTrue(
                    $"The rendered local target '{target}' returned {(int)response.StatusCode}."
                );
            }
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }

    private static bool Local(string target) =>
        !string.IsNullOrWhiteSpace(target)
        && !target.StartsWith('#')
        && !Uri.TryCreate(target, UriKind.Absolute, out _);

    [GeneratedRegex(
        "(?:href|src|data-theme-light-source|data-theme-dark-source)=\"([^\"]+)\"",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex LocalTarget();
}
