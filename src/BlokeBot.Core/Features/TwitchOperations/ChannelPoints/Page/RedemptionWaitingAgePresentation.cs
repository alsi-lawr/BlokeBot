namespace BlokeBot.Core.Features.TwitchOperations.ChannelPoints.Page;

internal enum RedemptionWaitingAgeBand
{
    Fresh,
    Waiting,
    NeedsAttention,
}

internal sealed record RedemptionWaitingAgePresentation(
    RedemptionWaitingAgeBand Band,
    TimeSpan Age,
    string Label,
    string BadgeClass
)
{
    private static readonly TimeSpan _waitingThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _needsAttentionThreshold = TimeSpan.FromMinutes(5);

    public string SemanticValue =>
        Band switch
        {
            RedemptionWaitingAgeBand.Fresh => "fresh",
            RedemptionWaitingAgeBand.Waiting => "waiting",
            RedemptionWaitingAgeBand.NeedsAttention => "needs-attention",
            _ => throw new ArgumentOutOfRangeException(nameof(Band)),
        };

    public string AccessibleLabel => $"Redemption {Label.ToLowerInvariant()}";

    public static RedemptionWaitingAgePresentation Create(
        DateTime redeemedAtUtc,
        TimeProvider timeProvider
    )
    {
        var utcRedeemedAt = DateTime.SpecifyKind(redeemedAtUtc, DateTimeKind.Utc);
        var age = timeProvider.GetUtcNow().UtcDateTime - utcRedeemedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var band = age switch
        {
            var value when value >= _needsAttentionThreshold =>
                RedemptionWaitingAgeBand.NeedsAttention,
            var value when value >= _waitingThreshold => RedemptionWaitingAgeBand.Waiting,
            _ => RedemptionWaitingAgeBand.Fresh,
        };
        var label = band switch
        {
            RedemptionWaitingAgeBand.Fresh => $"New · waiting {ElapsedText(age)}",
            RedemptionWaitingAgeBand.Waiting => $"Waiting · {ElapsedText(age)}",
            RedemptionWaitingAgeBand.NeedsAttention =>
                $"Needs attention · waiting {ElapsedText(age)}",
            _ => throw new ArgumentOutOfRangeException(nameof(band)),
        };
        var badgeClass = band switch
        {
            RedemptionWaitingAgeBand.Fresh =>
                "inline-flex rounded-full border border-sky-200 bg-sky-50 px-2.5 py-1 text-xs font-bold text-sky-700",
            RedemptionWaitingAgeBand.Waiting =>
                "inline-flex rounded-full border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-bold text-amber-700",
            RedemptionWaitingAgeBand.NeedsAttention =>
                "inline-flex rounded-full border border-rose-200 bg-rose-50 px-2.5 py-1 text-xs font-bold text-rose-700",
            _ => throw new ArgumentOutOfRangeException(nameof(band)),
        };

        return new(band, age, label, badgeClass);
    }

    private static string ElapsedText(TimeSpan age)
    {
        var totalMinutes = (int)Math.Floor(age.TotalMinutes);
        if (totalMinutes < 1)
        {
            return "less than a minute";
        }
        if (totalMinutes < 60)
        {
            return $"{totalMinutes} {Pluralize(totalMinutes, "minute")}";
        }

        var totalHours = totalMinutes / 60;
        var remainingMinutes = totalMinutes % 60;
        if (totalHours < 24)
        {
            return remainingMinutes == 0
                ? $"{totalHours} {Pluralize(totalHours, "hour")}"
                : $"{totalHours} {Pluralize(totalHours, "hour")} {remainingMinutes} {Pluralize(remainingMinutes, "minute")}";
        }

        var totalDays = totalHours / 24;
        var remainingHours = totalHours % 24;
        return remainingHours == 0
            ? $"{totalDays} {Pluralize(totalDays, "day")}"
            : $"{totalDays} {Pluralize(totalDays, "day")} {remainingHours} {Pluralize(remainingHours, "hour")}";
    }

    private static string Pluralize(int value, string singular) =>
        value == 1 ? singular : $"{singular}s";
}
