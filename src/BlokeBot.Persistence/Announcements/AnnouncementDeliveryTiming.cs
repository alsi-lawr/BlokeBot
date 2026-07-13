namespace BlokeBot.Announcements;

public sealed record AnnouncementRetryDelay
{
    public AnnouncementRetryDelay(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Retry delay must be positive.");
        }

        Value = value;
    }

    public TimeSpan Value { get; }
}

public sealed record AnnouncementOccurrenceLifetime
{
    public static readonly TimeSpan Maximum = TimeSpan.FromSeconds(60);

    public AnnouncementOccurrenceLifetime(TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Occurrence lifetime must be positive and no greater than {Maximum}."
            );
        }

        Value = value;
    }

    public TimeSpan Value { get; }
}
