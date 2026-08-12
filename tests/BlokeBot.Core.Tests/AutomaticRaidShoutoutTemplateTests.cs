using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutTemplateTests
{
    [Test]
    public void SixTokens_FallbacksAndRepeatedTokens_RenderDeterministically()
    {
        var parsed = AutomaticRaidShoutoutTemplate
            .Parse(
                "{twitch_handle}|{display_name}|{channel_url}|{viewer_count}|{last_game|unknown}|{stream_title|untitled}|{twitch_handle}"
            )
            .ShouldBeOfType<AutomaticRaidTemplateParseOutcome.Valid>();

        var rendered = parsed
            .Template.Render(new("@raider", "Raider", "https://twitch.tv/raider", 42, null, "  "))
            .ShouldBeOfType<AutomaticRaidTemplateRenderOutcome.Rendered>();

        rendered.Message.ShouldBe(
            "@raider|Raider|https://twitch.tv/raider|42|unknown|untitled|@raider"
        );
    }

    [Test]
    [Arguments("{unknown}")]
    [Arguments("{last_game}")]
    [Arguments("{stream_title|}")]
    [Arguments("{last_game|one|two}")]
    [Arguments("before}")]
    [Arguments("{after")]
    [Arguments("{{display_name}}")]
    public void MalformedOrUnknownSyntax_IsRejected(string source) =>
        AutomaticRaidShoutoutTemplate
            .Parse(source)
            .ShouldBeOfType<AutomaticRaidTemplateParseOutcome.Invalid>();

    [Test]
    public void AuthoredBudget_CountsLiteralsAndFallbacksButNotTokenSyntax()
    {
        var boundary = new string('a', 145) + "{last_game|12345}";
        AutomaticRaidShoutoutTemplate
            .Parse(boundary)
            .ShouldBeOfType<AutomaticRaidTemplateParseOutcome.Valid>()
            .Template.AuthoredCharacters.ShouldBe(150);

        _ = AutomaticRaidShoutoutTemplate
            .Parse(boundary + "x")
            .ShouldBeOfType<AutomaticRaidTemplateParseOutcome.Invalid>();
        AutomaticRaidShoutoutTemplate
            .Parse(string.Concat(Enumerable.Repeat("{display_name}", 100)))
            .ShouldBeOfType<AutomaticRaidTemplateParseOutcome.Valid>()
            .Template.AuthoredCharacters.ShouldBe(0);
    }

    [Test]
    public void RuntimeLimit_Accepts500AndRejects501WithoutTruncation()
    {
        var template = AutomaticRaidShoutoutTemplate
            .Parse("{display_name}")
            .ShouldBeOfType<AutomaticRaidTemplateParseOutcome.Valid>()
            .Template;
        template
            .Render(Values(new string('x', 500)))
            .ShouldBeOfType<AutomaticRaidTemplateRenderOutcome.Rendered>()
            .Message.Length.ShouldBe(500);
        var tooLong = template
            .Render(Values(new string('x', 501)))
            .ShouldBeOfType<AutomaticRaidTemplateRenderOutcome.TooLong>();
        tooLong.ActualCharacters.ShouldBe(501);
        tooLong.MaximumCharacters.ShouldBe(500);
    }

    private static AutomaticRaidTemplateValues Values(string displayName) =>
        new("@raider", displayName, "https://twitch.tv/raider", 1, null, null);
}
