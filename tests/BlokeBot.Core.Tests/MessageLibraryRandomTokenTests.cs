using System.Collections.Immutable;
using BlokeBot.Core.Features.CustomCommands;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MessageLibraryRandomTokenTests
{
    [Test]
    public async Task CommandReply_RendersEveryRandomOccurrenceAndKeepsContextCompatibility()
    {
        var random = new ScriptedRandomSource([2, 1, 0], [2]);
        var chatters = new RecordingChatterSource([
            new("alice-id", "alice", "Alice"),
            new("bob-id", "bob", "Bob"),
        ]);
        var renderer = new CustomCommandTemplateRenderer(random, chatters);

        var rendered = await renderer.RenderCommandAsync(
            "{random_from| red | blue | red}:{random_between|-2|2}:{random_viewer}:"
                + "{random_viewer}:{user}:{missing}",
            new(1, "streamer", "streamer-id"),
            Context(),
            [],
            null,
            CancellationToken.None
        );

        rendered
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe("red:2:Bob:Alice:viewer:{missing}");
        chatters.CallCount.ShouldBe(1);
    }

    [Test]
    public async Task ScheduledReply_LeavesContextOnlyTokensAndDoesNotFetchChatters()
    {
        var chatters = new RecordingChatterSource([]);
        var renderer = new CustomCommandTemplateRenderer(
            new ScriptedRandomSource([0], [4]),
            chatters
        );

        var rendered = await renderer.RenderScheduledAsync(
            "{user} {random_between|4|4} {unknown}",
            new(1, "streamer", "streamer-id"),
            CancellationToken.None
        );

        rendered
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe("{user} 4 {unknown}");
        chatters.CallCount.ShouldBe(0);
    }

    [Test]
    [Arguments("{random_from|one|two}", null)]
    [Arguments("{random_between|-1|1}", null)]
    [Arguments("{random_viewer}", null)]
    [Arguments("{other|one}", null)]
    [Arguments("{random_from}", "random_from needs at least one non-empty value.")]
    [Arguments("{random_from|one| }", "random_from needs at least one non-empty value.")]
    [Arguments("{random_between|1}", "random_between needs exactly two whole numbers.")]
    [Arguments("{random_between|2|1}", "random_between needs the lower number first.")]
    [Arguments("{random_viewer|one}", "random_viewer does not take values.")]
    [Arguments("{random_from", "Random message tokens need a closing brace.")]
    [Arguments("{random_between", "Random message tokens need a closing brace.")]
    [Arguments("{random_viewer", "Random message tokens need a closing brace.")]
    [Arguments("{random_from|one", "Random message tokens need a closing brace.")]
    [Arguments("{random_between|1|2", "Random message tokens need a closing brace.")]
    [Arguments("{random_viewer|one", "Random message tokens need a closing brace.")]
    [Arguments("{random_fromage", null)]
    [Arguments("{random_betweenish", null)]
    [Arguments("{random_viewer_notes", null)]
    public void Validation_OnlyRejectsMalformedRecognizedTokens(string template, string? error) =>
        MessageLibraryRandomTokenParser.Validate(template).ShouldBe(error);

    [Test]
    public async Task NestedFunctions_ComposeBothRandomKindsAndIndependentDraws()
    {
        var random = new ScriptedRandomSource([0, 0, 0], [-2, 2, 1, 2]);
        var renderer = new CustomCommandTemplateRenderer(random, new RecordingChatterSource([]));

        var rendered = await renderer.RenderCommandAsync(
            "{random_from|{random_between|{arg1}|{arg2}}}:"
                + "{random_between|{random_from|-2}|{random_between|2|2}}:"
                + "{random_from|{random_between|{arg1}|{arg2}}}",
            new(1, "streamer", "streamer-id"),
            Context(),
            ["-2", "2"],
            null,
            CancellationToken.None
        );

        rendered
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe("-2:1:2");
        random.Bounds.ShouldBe([(-2, 2), (2, 2), (-2, 2), (-2, 2)]);
    }

    [Test]
    [Arguments("one|two")]
    [Arguments("{random_between|1|1}")]
    [Arguments("")]
    public async Task NestedFrom_ResolvedArgumentIsOpaqueDataIncludingEmptyText(string argument)
    {
        var random = new ScriptedRandomSource([0], []);
        var renderer = new CustomCommandTemplateRenderer(random, new RecordingChatterSource([]));

        var rendered = await renderer.RenderCommandAsync(
            "{random_from|{arg1}}",
            new(1, "streamer", "streamer-id"),
            Context(),
            [argument],
            null,
            CancellationToken.None
        );

        rendered
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe(argument);
        random.Bounds.ShouldBeEmpty();
        random.ChoiceCounts.ShouldBe([1]);
    }

    [Test]
    public async Task NestedContext_ResolvedTextCannotIntroduceRandomSyntaxOrChoices()
    {
        var random = new ScriptedRandomSource([0], []);
        var renderer = new CustomCommandTemplateRenderer(random, new RecordingChatterSource([]));
        var context = new ChatCommandContext
        {
            Message = new ChatMessage(
                "Viewer|{random_between|1|1}",
                "streamer",
                "!command",
                "!command",
                new Dictionary<string, string>()
            ),
            CommandName = "command",
            Responder = static (_, _) => ValueTask.CompletedTask,
        };
        var rendered = await renderer.RenderCommandAsync(
            "{random_from|{user}}",
            new(1, "streamer", "streamer-id"),
            context,
            [],
            null,
            CancellationToken.None
        );

        rendered
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe("viewer|{random_between|1|1}");
        random.Bounds.ShouldBeEmpty();
        random.ChoiceCounts.ShouldBe([1]);
    }

    [Test]
    public async Task NestedViewers_ShareLookupAndKeepChatterTextOpaque()
    {
        const string Chatter = "Name|{random_between|1|1}";
        var random = new ScriptedRandomSource([0, 0, 0, 0], []);
        var chatters = new RecordingChatterSource([new("id", "viewer", Chatter)]);
        var renderer = new CustomCommandTemplateRenderer(random, chatters);

        var rendered = await renderer.RenderCommandAsync(
            "{random_from|{random_viewer}}:{random_from|{random_viewer}}",
            new(1, "streamer", "streamer-id"),
            Context(),
            [],
            null,
            CancellationToken.None
        );

        rendered
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe($"{Chatter}:{Chatter}");
        chatters.CallCount.ShouldBe(1);
        random.Bounds.ShouldBeEmpty();
        random.ChoiceCounts.ShouldBe([1, 1, 1, 1]);
    }

    [Test]
    public async Task NestedViewerNumericBound_UsesSelectedNumberAndUnavailableViewerFailsOnlyNumericUse()
    {
        var random = new ScriptedRandomSource([0], [10]);
        var renderer = new CustomCommandTemplateRenderer(
            random,
            new RecordingChatterSource([new("id", "viewer", "10")])
        );
        var numbered = await renderer.RenderScheduledAsync(
            "{random_between|1|{random_viewer}}",
            new(1, "streamer", "streamer-id"),
            CancellationToken.None
        );
        numbered.Match(static text => text, static failure => failure.ChatMessage()).ShouldBe("10");
        random.Bounds.ShouldBe([(1, 10)]);

        renderer = new CustomCommandTemplateRenderer(
            new ScriptedRandomSource([0], []),
            new RecordingChatterSource([])
        );
        var empty = await renderer.RenderScheduledAsync(
            "{random_from|{random_viewer}}",
            new(1, "streamer", "streamer-id"),
            CancellationToken.None
        );
        empty
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe(string.Empty);
        var numeric = await renderer.RenderScheduledAsync(
            "prefix {random_between|1|{random_viewer}} suffix",
            new(1, "streamer", "streamer-id"),
            CancellationToken.None
        );
        numeric
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldContain("missing");
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task EmptyNumericArgument_WithEarlierSuccessfulDraw_WithholdsWholeAffectedTemplate(
        string argument
    )
    {
        var random = new ScriptedRandomSource([0], [1]);
        var renderer = new CustomCommandTemplateRenderer(random, new RecordingChatterSource([]));
        var rendered = await renderer.RenderCommandAsync(
            "prefix {random_between|1|1} {random_from|{random_between|1|{arg1}}} suffix",
            new(1, "streamer", "streamer-id"),
            Context(),
            [argument],
            null,
            CancellationToken.None
        );

        var reply = rendered.Match(static text => text, static failure => failure.ChatMessage());
        reply.ShouldContain("missing");
        reply.ShouldNotContain("prefix");
        reply.ShouldNotContain("suffix");
        random.Bounds.ShouldBe([(1, 1)]);
    }

    [Test]
    public async Task ScheduledContextAbsenceAndUnknownPrefixes_RemainDataExceptAsNumericBounds()
    {
        var renderer = new CustomCommandTemplateRenderer(
            new ScriptedRandomSource([0], []),
            new RecordingChatterSource([])
        );
        var preserved = await renderer.RenderScheduledAsync(
            "{random_from|{user}:{arg1}:{count}} {random_fromage|one} {arg}",
            new(1, "streamer", "streamer-id"),
            CancellationToken.None
        );
        preserved
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldBe("{user}:{arg1}:{count} {random_fromage|one} {arg}");
        var numeric = await renderer.RenderScheduledAsync(
            "{random_between|1|{arg1}}",
            new(1, "streamer", "streamer-id"),
            CancellationToken.None
        );
        numeric
            .Match(static text => text, static failure => failure.ChatMessage())
            .ShouldContain("whole");
    }

    [Test]
    public void CommandPreview_NestedStructure_SubstitutesOnlyContextWithoutDrawing() =>
        CustomCommandTemplateRenderer
            .RenderCommandPreview(
                "{random_from|{random_between|1|{arg1}}|{user}|{random_viewer}}",
                Context(),
                ["10"],
                null
            )
            .ShouldBe("{random_from|{random_between|1|10}|viewer|{random_viewer}}");

    private static ChatCommandContext Context() =>
        new()
        {
            Message = new ChatMessage(
                "viewer",
                "streamer",
                "!command",
                "!command",
                new Dictionary<string, string>()
            ),
            CommandName = "command",
            Responder = static (_, _) => ValueTask.CompletedTask,
        };

    private sealed class RecordingChatterSource(ImmutableArray<HelixChatter> available)
        : IMessageLibraryChatterSource
    {
        public int CallCount { get; private set; }

        public Task<ImmutableArray<HelixChatter>> GetAsync(
            MessageLibraryRenderHost host,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(available);
        }
    }

    private sealed class ScriptedRandomSource(IEnumerable<int> indexes, IEnumerable<int> integers)
        : IMessageLibraryRandomSource
    {
        private readonly Queue<int> _indexes = new(indexes);
        private readonly Queue<int> _integers = new(integers);

        public List<int> ChoiceCounts { get; } = [];
        public List<(int Minimum, int Maximum)> Bounds { get; } = [];

        public int Next(int exclusiveMaximum)
        {
            ChoiceCounts.Add(exclusiveMaximum);
            var index = _indexes.Dequeue();
            index.ShouldBeInRange(0, exclusiveMaximum - 1);
            return index;
        }

        public int NextInclusive(int minimum, int maximum)
        {
            Bounds.Add((minimum, maximum));
            var integer = _integers.Dequeue();
            integer.ShouldBeInRange(minimum, maximum);
            return integer;
        }
    }
}
