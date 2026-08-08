using System.Collections.Immutable;
using System.Globalization;
using Cel;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationExpressionCheck
{
    private AutomationExpressionCheck() { }

    internal sealed record Valid : AutomationExpressionCheck;

    internal sealed record Invalid : AutomationExpressionCheck;
}

internal abstract record AutomationExpressionResult
{
    private AutomationExpressionResult() { }

    internal sealed record Value(object? Result, bool UsesSensitiveValues)
        : AutomationExpressionResult;

    internal sealed record Invalid : AutomationExpressionResult;
}

public sealed class AutomationExpressionService
{
    private readonly CelEnvironment _environment = new([], string.Empty);

    internal AutomationExpressionCheck Validate(AutomationExpressionSource expression)
    {
        if (expression.LanguageVersion != AutomationExpressionLanguage.CurrentVersion)
        {
            return new AutomationExpressionCheck.Invalid();
        }

        try
        {
            _ = _environment.Compile(expression.Source);
            return new AutomationExpressionCheck.Valid();
        }
        catch (CelException)
        {
            return new AutomationExpressionCheck.Invalid();
        }
    }

    internal AutomationExpressionResult Evaluate(
        AutomationExpressionSource expression,
        AutomationContext context
    )
    {
        if (expression.LanguageVersion != AutomationExpressionLanguage.CurrentVersion)
        {
            return new AutomationExpressionResult.Invalid();
        }

        try
        {
            var evaluation = EvaluationContext(context);
            var program = _environment.Compile(expression.Source);
            return new AutomationExpressionResult.Value(
                program(evaluation.Values),
                evaluation.SensitiveIdentifiers.Any(identifier =>
                    ContainsIdentifier(expression.Source, identifier)
                )
            );
        }
        catch (CelException)
        {
            return new AutomationExpressionResult.Invalid();
        }
    }

    internal AutomationExpressionResult Interpolate(string template, AutomationContext context)
    {
        var output = new System.Text.StringBuilder();
        var usesSensitiveValues = false;
        var offset = 0;
        while (offset < template.Length)
        {
            var start = template.IndexOf("${", offset, StringComparison.Ordinal);
            if (start < 0)
            {
                _ = output.Append(template, offset, template.Length - offset);
                break;
            }

            _ = output.Append(template, offset, start - offset);
            var end = template.IndexOf('}', start + 2);
            if (end < 0)
            {
                return new AutomationExpressionResult.Invalid();
            }

            var source = template[(start + 2)..end];
            var result = Evaluate(
                new(AutomationExpressionLanguage.CurrentVersion, source),
                context
            );
            if (result is not AutomationExpressionResult.Value value)
            {
                return new AutomationExpressionResult.Invalid();
            }

            usesSensitiveValues |= value.UsesSensitiveValues;
            _ = output.Append(ToInvariantText(value.Result));
            offset = end + 1;
        }

        return new AutomationExpressionResult.Value(output.ToString(), usesSensitiveValues);
    }

    internal AutomationExpressionCheck ValidateTemplate(string template)
    {
        var offset = 0;
        while (offset < template.Length)
        {
            var start = template.IndexOf("${", offset, StringComparison.Ordinal);
            if (start < 0)
            {
                return new AutomationExpressionCheck.Valid();
            }

            var end = template.IndexOf('}', start + 2);
            if (
                end < 0
                || Validate(
                    new(AutomationExpressionLanguage.CurrentVersion, template[(start + 2)..end])
                ) is AutomationExpressionCheck.Invalid
            )
            {
                return new AutomationExpressionCheck.Invalid();
            }

            offset = end + 1;
        }

        return new AutomationExpressionCheck.Valid();
    }

    private static AutomationEvaluationContext EvaluationContext(AutomationContext context)
    {
        Dictionary<string, object?> values = new(StringComparer.Ordinal)
        {
            ["event"] = new Dictionary<string, object?>
            {
                ["occurrence_id"] = context.Event.OccurrenceId.ToString("D"),
                ["source"] = context.Event.SourceDefinitionId.Value,
            },
            ["actor"] = context.Actor is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["twitch_user_id"] = context.Actor.TwitchUserId,
                    ["login"] = context.Actor.Login,
                    ["display_name"] = context.Actor.DisplayName,
                },
            ["channel"] = new Dictionary<string, object?>
            {
                ["host_id"] = context.Channel.HostId.Value,
                ["twitch_channel_id"] = context.Channel.TwitchChannelId,
                ["login"] = context.Channel.Login,
                ["display_name"] = context.Channel.DisplayName,
            },
            ["stream"] = context.Stream is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["twitch_stream_id"] = context.Stream.TwitchStreamId,
                    ["title"] = context.Stream.Title,
                    ["game_name"] = context.Stream.GameName,
                    ["started_at"] = context.Stream.StartedAtUtc,
                },
            ["timestamps"] = new Dictionary<string, object?>
            {
                ["occurred_at"] = context.Timestamps.OccurredAtUtc,
                ["received_at"] = context.Timestamps.ReceivedAtUtc,
            },
            ["arguments"] = context
                .Arguments.OrderBy(static argument => argument.Position)
                .Select(static argument => argument.Value)
                .ToArray(),
        };
        var sensitive = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        _ = sensitive.Add("arguments");
        foreach (var (name, variable) in context.Variables.ForExecution())
        {
            values[name.Value] = ToCelValue(variable.Value);
            if (variable.Sensitivity == AutomationDataSensitivity.Sensitive)
            {
                _ = sensitive.Add(name.Value);
            }
        }

        return new(values, sensitive.ToImmutable());
    }

    private static object? ToCelValue(AutomationValue value) =>
        value switch
        {
            AutomationValue.Text text => text.Value,
            AutomationValue.Number number => number.Value,
            AutomationValue.Boolean boolean => boolean.Value,
            AutomationValue.Timestamp timestamp => timestamp.Value,
            AutomationValue.Actor actor => new Dictionary<string, object?>
            {
                ["twitch_user_id"] = actor.Value.TwitchUserId,
                ["login"] = actor.Value.Login,
                ["display_name"] = actor.Value.DisplayName,
            },
            AutomationValue.Channel channel => new Dictionary<string, object?>
            {
                ["host_id"] = channel.Value.HostId.Value,
                ["twitch_channel_id"] = channel.Value.TwitchChannelId,
                ["login"] = channel.Value.Login,
                ["display_name"] = channel.Value.DisplayName,
            },
            AutomationValue.Stream stream => new Dictionary<string, object?>
            {
                ["twitch_stream_id"] = stream.Value.TwitchStreamId,
                ["title"] = stream.Value.Title,
                ["game_name"] = stream.Value.GameName,
                ["started_at"] = stream.Value.StartedAtUtc,
            },
            _ => null,
        };

    private static bool ContainsIdentifier(string source, string identifier)
    {
        var offset = 0;
        while ((offset = source.IndexOf(identifier, offset, StringComparison.Ordinal)) >= 0)
        {
            var before = offset == 0 || !IsIdentifierCharacter(source[offset - 1]);
            var after =
                offset + identifier.Length == source.Length
                || !IsIdentifierCharacter(source[offset + identifier.Length]);
            if (before && after)
            {
                return true;
            }

            offset += identifier.Length;
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value == '_';

    private static string ToInvariantText(object? value) =>
        value switch
        {
            null => string.Empty,
            string text => text,
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private sealed record AutomationEvaluationContext(
        Dictionary<string, object?> Values,
        ImmutableHashSet<string> SensitiveIdentifiers
    );
}
