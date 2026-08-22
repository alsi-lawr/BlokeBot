using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal interface IOverlayConfigurationTransferAdapter
{
    Task<ConfigurationSectionPreview> PreviewAsync(
        BlokeBotDbContext db,
        BotHost host,
        OverlaysSectionV1? section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<ConfigurationValidationIssue>> StageAsync(
        BlokeBotDbContext db,
        BotHost host,
        OverlaysSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    );
}

internal interface IAutomationConfigurationTransferAdapter
{
    Task<ConfigurationSectionPreview> PreviewAsync(
        BlokeBotDbContext db,
        BotHost host,
        AutomationsSectionV1? section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    );

    Task<AutomationConfigurationStageResult> StageAsync(
        BlokeBotDbContext db,
        BotHost host,
        AutomationsSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    );
}

internal sealed record AutomationConfigurationStageResult(
    IReadOnlyList<ConfigurationValidationIssue> Issues,
    IReadOnlyList<AutomationTransferDiagnostic> Diagnostics
);

internal sealed class UnavailableOverlayConfigurationTransferAdapter
    : IOverlayConfigurationTransferAdapter
{
    internal static UnavailableOverlayConfigurationTransferAdapter Instance { get; } = new();

    public Task<ConfigurationSectionPreview> PreviewAsync(
        BlokeBotDbContext db,
        BotHost host,
        OverlaysSectionV1? section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    ) => Task.FromResult(Preview());

    public Task<IReadOnlyList<ConfigurationValidationIssue>> StageAsync(
        BlokeBotDbContext db,
        BotHost host,
        OverlaysSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    ) => Task.FromResult<IReadOnlyList<ConfigurationValidationIssue>>([Issue()]);

    private static ConfigurationSectionPreview Preview() =>
        new(ConfigurationSectionId.Overlays, new(0, 0, 0, 0), [Issue()], []);

    private static ConfigurationValidationIssue Issue() =>
        new("sections.overlays", "Overlay configuration transfer is unavailable.");
}

internal sealed class UnavailableAutomationConfigurationTransferAdapter
    : IAutomationConfigurationTransferAdapter
{
    internal static UnavailableAutomationConfigurationTransferAdapter Instance { get; } = new();

    public Task<ConfigurationSectionPreview> PreviewAsync(
        BlokeBotDbContext db,
        BotHost host,
        AutomationsSectionV1? section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    ) => Task.FromResult(Preview());

    public Task<AutomationConfigurationStageResult> StageAsync(
        BlokeBotDbContext db,
        BotHost host,
        AutomationsSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    ) => Task.FromResult(new AutomationConfigurationStageResult([Issue()], []));

    private static ConfigurationSectionPreview Preview() =>
        new(ConfigurationSectionId.Automations, new(0, 0, 0, 0), [Issue()], []);

    private static ConfigurationValidationIssue Issue() =>
        new("sections.automations", "Automation configuration transfer is unavailable.");
}
