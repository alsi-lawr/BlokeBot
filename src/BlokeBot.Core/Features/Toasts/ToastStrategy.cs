namespace BlokeBot.Core.Features.Toasts;

public interface IToastStrategy
{
    static abstract ToastKind Kind { get; }

    static abstract string DefaultTitle { get; }

    static abstract ToastTone Tone { get; }

    static abstract ToastDismissal Dismissal { get; }
}

public readonly record struct ToastDismissal
{
    private ToastDismissal(TimeSpan? autoDismissAfter)
    {
        AutoDismissAfter = autoDismissAfter;
    }

    internal TimeSpan? AutoDismissAfter { get; }

    public static ToastDismissal Manual => new(null);

    public static ToastDismissal Automatic(TimeSpan after)
    {
        if (after <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(after),
                "Dismissal time must be positive."
            );
        }

        return new(after);
    }
}

public readonly struct StatusToastStrategy : IToastStrategy
{
    public static ToastKind Kind => ToastKind.Status;

    public static string DefaultTitle => "Status";

    public static ToastTone Tone => ToastTone.Neutral;

    public static ToastDismissal Dismissal => ToastDismissal.Automatic(TimeSpan.FromSeconds(4));
}

public readonly struct SuccessToastStrategy : IToastStrategy
{
    public static ToastKind Kind => ToastKind.Success;

    public static string DefaultTitle => "Done";

    public static ToastTone Tone => ToastTone.Positive;

    public static ToastDismissal Dismissal => ToastDismissal.Automatic(TimeSpan.FromSeconds(4));
}

public readonly struct WarningToastStrategy : IToastStrategy
{
    public static ToastKind Kind => ToastKind.Warning;

    public static string DefaultTitle => "Needs attention";

    public static ToastTone Tone => ToastTone.Caution;

    public static ToastDismissal Dismissal => ToastDismissal.Manual;
}

public readonly struct ErrorToastStrategy : IToastStrategy
{
    public static ToastKind Kind => ToastKind.Error;

    public static string DefaultTitle => "Something went wrong";

    public static ToastTone Tone => ToastTone.Critical;

    public static ToastDismissal Dismissal => ToastDismissal.Manual;
}

public readonly struct PositiveStatusToastStrategy : IToastStrategy
{
    public static ToastKind Kind => ToastKind.Status;

    public static string DefaultTitle => "Status";

    public static ToastTone Tone => ToastTone.Positive;

    public static ToastDismissal Dismissal => ToastDismissal.Automatic(TimeSpan.FromSeconds(4));
}

public readonly struct CautionStatusToastStrategy : IToastStrategy
{
    public static ToastKind Kind => ToastKind.Status;

    public static string DefaultTitle => "Status";

    public static ToastTone Tone => ToastTone.Caution;

    public static ToastDismissal Dismissal => ToastDismissal.Automatic(TimeSpan.FromSeconds(4));
}
