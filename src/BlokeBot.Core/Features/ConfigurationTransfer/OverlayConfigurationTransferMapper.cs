using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class OverlayConfigurationTransferMapper
{
    internal static async Task<OverlayConfigurationMapOutcome> MapAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlayInstanceV1 instance,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var value = instance.Type switch
            {
                OverlayType.Empty when Empty(instance.Configuration) =>
                    new OverlayConfiguration.EmptyV1(),
                OverlayType.CuePlayer when Empty(instance.Configuration) =>
                    new OverlayConfiguration.CuePlayerV1(),
                OverlayType.Guessing => new OverlayConfiguration.GuessingV1(
                    Required(instance.Configuration.ShowGuessCount),
                    Required(instance.Configuration.ResultDurationSeconds),
                    Appearance(instance.Configuration)
                ),
                OverlayType.Giveaway => new OverlayConfiguration.GiveawayV1(
                    Required(instance.Configuration.Title),
                    Required(instance.Configuration.ShowEntrantCount),
                    Required(instance.Configuration.ShowCountdown),
                    Required(instance.Configuration.ShowJoinCommand),
                    Appearance(instance.Configuration)
                ),
                OverlayType.EventFeed => new OverlayConfiguration.EventFeedV1(
                    Required(instance.Configuration.Capacity),
                    Required(instance.Configuration.OverflowPolicy),
                    Required(instance.Configuration.EventKinds)
                        .ToDictionary(
                            value => value.Kind,
                            value => new EventFeedKindConfiguration(
                                value.Enabled,
                                value.Template,
                                value.Priority,
                                value.DurationSeconds
                            )
                        ),
                    Appearance(instance.Configuration)
                ),
                OverlayType.ViewerQueue => await ViewerQueueAsync(
                    db,
                    hostId,
                    instance.Configuration,
                    cancellationToken
                ),
                _ => throw new ArgumentException("The overlay configuration shape is invalid."),
            };
            return new OverlayConfigurationMapOutcome.Mapped(value);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            return new OverlayConfigurationMapOutcome.Invalid(
                $"{instance.Name}: {exception.Message}"
            );
        }
    }

    private static async Task<OverlayConfiguration> ViewerQueueAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlayConfigurationV1 configuration,
        CancellationToken cancellationToken
    )
    {
        var normalized = ConfigurationImportReferencePlan.NormalizeName(
            Required(configuration.ViewerQueueName)
        );
        var queues = await db
            .PlayQueues.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new { value.Id, value.Name })
            .ToArrayAsync(cancellationToken);
        var matches = queues
            .Where(value =>
                ConfigurationImportReferencePlan.NormalizeName(value.Name) == normalized
            )
            .ToArray();
        return matches.Length != 1
            ? throw new ArgumentException(
                "The referenced Viewer Queue does not have one explicit normalized-name match."
            )
            : new OverlayConfiguration.ViewerQueueV1(
                matches[0].Id,
                Required(configuration.CurrentRows),
                Required(configuration.NextRows),
                Appearance(configuration)
            );
    }

    private static OverlayAppearance Appearance(OverlayConfigurationV1 configuration)
    {
        var value = Required(configuration.Appearance);
        return new(value.X, value.Y, value.Width, value.Height, value.Css);
    }

    private static bool Empty(OverlayConfigurationV1 configuration) =>
        configuration == new OverlayConfigurationV1(1);

    private static T Required<T>(T? value)
        where T : struct =>
        value ?? throw new ArgumentException("A required overlay configuration value is missing.");

    private static T Required<T>(T? value)
        where T : class =>
        value ?? throw new ArgumentException("A required overlay configuration value is missing.");
}

internal abstract record OverlayConfigurationMapOutcome
{
    private OverlayConfigurationMapOutcome() { }

    internal sealed record Mapped(OverlayConfiguration Configuration)
        : OverlayConfigurationMapOutcome;

    internal sealed record Invalid(string Message) : OverlayConfigurationMapOutcome;
}
