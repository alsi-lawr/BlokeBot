using BlokeBot.Features.Guessing.Replies;

namespace BlokeBot.Features.Points.Replies;

public static class PointsTemplateFormatter
{
    public static string Format(string template, IReadOnlyDictionary<string, string> values) =>
        TemplateFormatter.Format(template, values);
}
