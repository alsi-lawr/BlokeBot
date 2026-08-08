using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static class CustomCommandAutomationContext
{
    internal const int MaximumRawArgumentsLength = 500;
    internal const int MaximumArgumentCount = 50;
    internal const int MaximumArgumentLength = 500;
    internal const int MaximumMessageIdLength = 128;

    internal static AutomationContext Create(
        BotHost host,
        CustomCommand command,
        string aliasUsed,
        ChatMessage message,
        IReadOnlyList<string> arguments,
        string? activeStreamId,
        DateTimeOffset receivedAtUtc
    )
    {
        var twitchMessageId = TwitchMessageId(message);
        var actor = new AutomationActor(
            Tag(message, "user-id"),
            message.Login,
            Tag(message, "display-name") switch
            {
                [] => message.Login,
                var displayName => displayName,
            }
        );
        var channel = new AutomationChannel(
            new(host.Id),
            host.TwitchUserId ?? string.Empty,
            host.Login,
            string.IsNullOrWhiteSpace(host.DisplayName) ? host.Login : host.DisplayName
        );
        var stream = string.IsNullOrWhiteSpace(activeStreamId)
            ? null
            : new AutomationStream(activeStreamId, null, null, null);
        var parsedArguments = arguments
            .Take(MaximumArgumentCount)
            .Select(
                static (argument, index) =>
                    new AutomationArgument(index, Bound(argument, MaximumArgumentLength))
            )
            .ToImmutableArray();
        return new(
            new(
                OccurrenceId(host.Id, twitchMessageId),
                AutomationDefinitionIds.CustomCommandSource
            ),
            actor,
            channel,
            stream,
            new(OccurredAtUtc(message, receivedAtUtc), receivedAtUtc),
            parsedArguments,
            new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("command_id")] = SafeNumber(command.Id),
                    [new("command_name")] = SafeText(command.Name),
                    [new("command_alias")] = SafeText(aliasUsed),
                    [new("raw_arguments")] = SensitiveText(RawArguments(message.Text)),
                    [new("viewer_is_moderator")] = SensitiveBoolean(
                        ChatModeratorPolicy.IsModerator(message)
                    ),
                    [new("viewer_is_subscriber")] = SensitiveBoolean(
                        string.Equals(Tag(message, "subscriber"), "1", StringComparison.Ordinal)
                    ),
                    [new("twitch_message_id")] = SensitiveText(twitchMessageId ?? string.Empty),
                }
            )
        );
    }

    private static Guid OccurrenceId(int hostId, string? twitchMessageId)
    {
        if (twitchMessageId is null)
        {
            // Without a bounded Twitch identity, each delivery is independent rather than falsely deduplicated.
            return Guid.NewGuid();
        }

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{hostId.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{twitchMessageId}"
            )
        );
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string? TwitchMessageId(ChatMessage message)
    {
        var value = Tag(message, "id");
        return value.Length is > 0 and <= MaximumMessageIdLength ? value : null;
    }

    private static DateTimeOffset OccurredAtUtc(ChatMessage message, DateTimeOffset receivedAtUtc)
    {
        var timestamp = Tag(message, "tmi-sent-ts");
        if (!long.TryParse(timestamp, out var milliseconds))
        {
            return receivedAtUtc;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return receivedAtUtc;
        }
    }

    private static string RawArguments(string message)
    {
        var commandEnd = message.AsSpan().IndexOfAny(" \t\r\n");
        return commandEnd < 0
            ? string.Empty
            : Bound(message[(commandEnd + 1)..].TrimStart(), MaximumRawArgumentsLength);
    }

    private static string Tag(ChatMessage message, string name) =>
        message.Tags.TryGetValue(name, out var value) ? value : string.Empty;

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static AutomationVariable SafeText(string value) =>
        new(new AutomationValue.Text(value), AutomationDataSensitivity.Safe);

    private static AutomationVariable SafeNumber(int value) =>
        new(new AutomationValue.Number(value), AutomationDataSensitivity.Safe);

    private static AutomationVariable SensitiveText(string value) =>
        new(new AutomationValue.Text(value), AutomationDataSensitivity.Sensitive);

    private static AutomationVariable SensitiveBoolean(bool value) =>
        new(new AutomationValue.Boolean(value), AutomationDataSensitivity.Sensitive);
}
