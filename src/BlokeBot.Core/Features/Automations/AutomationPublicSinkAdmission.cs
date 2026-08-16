namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationPublicTextAdmission
{
    private AutomationPublicTextAdmission() { }

    internal sealed record Admitted(string Text) : AutomationPublicTextAdmission;

    internal sealed record Blocked : AutomationPublicTextAdmission;
}

internal static class AutomationPublicSinkAdmission
{
    internal static AutomationPublicTextAdmission AdmitText(AutomationResolvedValue value) =>
        value.Value is AutomationValue.Text text
        && !value.Provenance.IsDefaultOrEmpty
        && value.Provenance.All(Enum.IsDefined)
            ? new AutomationPublicTextAdmission.Admitted(text.Value)
            : new AutomationPublicTextAdmission.Blocked();

    internal static AutomationPublicTextAdmission AdmitText(
        AutomationExpressionResult.Value value
    ) =>
        value is { Result: string text, UsesSensitiveValues: false }
            ? new AutomationPublicTextAdmission.Admitted(text)
            : new AutomationPublicTextAdmission.Blocked();
}
