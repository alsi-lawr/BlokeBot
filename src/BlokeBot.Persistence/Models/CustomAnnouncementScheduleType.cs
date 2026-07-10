namespace BlokeBot.Persistence.Models;

public enum CustomAnnouncementScheduleType
{
    Interval,
    IntervalAfterChat,
    Weekly,
}

public static class CustomAnnouncementScheduleTypeStore
{
    public static IReadOnlyList<string> Values { get; } =
    [
        Format(CustomAnnouncementScheduleType.Interval),
        Format(CustomAnnouncementScheduleType.IntervalAfterChat),
        Format(CustomAnnouncementScheduleType.Weekly),
    ];

    public static string Format(CustomAnnouncementScheduleType type) => type.ToString();

    public static CustomAnnouncementScheduleType Parse(string value) =>
        Enum.TryParse<CustomAnnouncementScheduleType>(value, ignoreCase: true, out var type)
            ? type
            : throw new FormatException($"Unknown custom announcement schedule type '{value}'.");
}
