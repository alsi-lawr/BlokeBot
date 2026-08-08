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

        rendered.ShouldBe("red:2:Bob:Alice:viewer:{missing}");
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

        rendered.ShouldBe("{user} 4 {unknown}");
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

        public int Next(int exclusiveMaximum) => _indexes.Dequeue();

        public int NextInclusive(int minimum, int maximum) => _integers.Dequeue();
    }
}
