namespace BlokeBot.Text;

public static class TemplateFormatter
{
    public static string Format(string template, IReadOnlyDictionary<string, string> values)
    {
        var formatted = template;
        foreach (var (key, value) in values)
            formatted = formatted.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);

        return formatted;
    }
}
