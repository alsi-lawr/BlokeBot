using System.Globalization;
using System.Text.RegularExpressions;
using BlokeBot.Commands;
using BlokeBot.Twitch;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandTemplateRenderer
{
    private static readonly Regex _tokenPattern = new(
        @"\{([A-Za-z0-9_]+)\}",
        RegexOptions.CultureInvariant
    );

    public string Render(
        string template,
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        long? count
    )
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = Login.Normalize(context.Message.Login),
            ["channel"] = Login.Normalize(context.Message.Channel),
            ["command"] = CommandAliasNormalizer.Normalize(context.CommandName),
            ["args"] = string.Join(' ', args),
        };

        for (var i = 0; i < 9; i++)
        {
            values[$"arg{i + 1}"] = i < args.Count ? args[i] : string.Empty;
        }

        if (count is not null)
        {
            values["count"] = count.Value.ToString(CultureInfo.InvariantCulture);
        }

        return _tokenPattern.Replace(
            template,
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value
        );
    }
}
