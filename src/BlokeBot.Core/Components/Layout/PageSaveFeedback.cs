namespace BlokeBot.Core.Components.Layout;

public sealed record PageSaveFeedback(string Message, PageSaveFeedbackKind Kind);

public enum PageSaveFeedbackKind
{
    Dirty,
    Saving,
    Validation,
    Success,
    Failure,
}
