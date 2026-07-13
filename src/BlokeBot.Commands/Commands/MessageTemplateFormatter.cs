namespace BlokeBot.Commands;

/// <summary>
/// Applies simple case-insensitive token replacement to bot reply templates.
/// </summary>
public static class MessageTemplateFormatter
{
    public static string Format(string template, IReadOnlyDictionary<string, string> values)
    {
        var formatted = template;
        foreach (var (key, value) in values)
        {
            formatted = formatted.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        }

        return formatted;
    }
}
