using BlokeBot.Core.Auth.Sessions;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public static class ConfigurationTransferEndpoints
{
    public static void MapConfigurationTransferEndpoints(this WebApplication app) =>
        _ = app.MapGet(
                "/configuration-transfer/export",
                async (
                    HttpContext context,
                    string? sections,
                    bool? overlayUrls,
                    bool? overlayMedia,
                    bool? urlWarningAcknowledged,
                    BlokeBotPageContextAccessor pageContexts,
                    ConfigurationDocumentExporter exporter,
                    CancellationToken cancellationToken
                ) =>
                {
                    var page = pageContexts.FromPrincipal(context.User);
                    var host = page.Session.State.Match<Hosts.BotHostChoice?>(
                        _ => null,
                        selected => selected.Selection.Current,
                        _ => null
                    );
                    if (host is null || page.IsBotAccount)
                    {
                        return Results.Forbid();
                    }
                    var selected = ParseSections(sections);
                    return selected.Count == 0
                        ? Results.BadRequest("Choose at least one configuration section.")
                        : await exporter.ExportAsync(
                            host.Id,
                            new(
                                selected,
                                new(
                                    overlayUrls.GetValueOrDefault(),
                                    overlayMedia.GetValueOrDefault(),
                                    urlWarningAcknowledged.GetValueOrDefault()
                                )
                            ),
                            cancellationToken
                        ) switch
                        {
                            ConfigurationExportOutcome.Success success => Results.File(
                                success.Json,
                                "application/json",
                                $"blokebot-{host.Login}-configuration-v1.json"
                            ),
                            ConfigurationExportOutcome.NotFound => Results.NotFound(),
                            ConfigurationExportOutcome.Unsupported unsupported =>
                                Results.UnprocessableEntity(unsupported.Message),
                            _ => Results.UnprocessableEntity(),
                        };
                }
            )
            .RequireAuthorization("Operator");

    private static HashSet<ConfigurationSectionId> ParseSections(string? value)
    {
        var selected = new HashSet<ConfigurationSectionId>();
        foreach (
            var token in (value ?? string.Empty).Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (Enum.TryParse<ConfigurationSectionId>(token, true, out var section))
            {
                _ = selected.Add(section);
            }
        }
        return selected;
    }
}
