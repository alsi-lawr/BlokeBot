using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferImportProgress
{
    [Parameter, EditorRequired]
    public ConfigurationImportStage Stage { get; set; }

    private bool IsCurrent(ConfigurationImportStage step) =>
        Stage != ConfigurationImportStage.Applied && Stage == step;

    private string PillClass(ConfigurationImportStage step) =>
        Stage == ConfigurationImportStage.Applied || (int)step < (int)Stage
            ? "status-pill status-pill--green"
        : step == Stage ? "status-pill status-pill--blue"
        : "status-pill status-pill--slate";

    private string StepLabel(ConfigurationImportStage step, string label) =>
        Stage == ConfigurationImportStage.Applied || (int)step < (int)Stage
            ? $"✓ {label}"
            : $"{(int)step} · {label}";
}

public enum ConfigurationImportStage
{
    File = 1,
    Review = 2,
    Apply = 3,
    Applied = 4,
}
