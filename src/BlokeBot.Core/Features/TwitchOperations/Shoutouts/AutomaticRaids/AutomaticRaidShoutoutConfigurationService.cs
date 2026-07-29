using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

public sealed class AutomaticRaidShoutoutConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
)
{
    public async Task<AutomaticRaidShoutoutConfiguration?> LoadAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Hosts.AsNoTracking().AnyAsync(host => host.Id == hostId, cancellationToken))
        {
            return null;
        }
        var settings = await db
            .AutomaticRaidShoutoutSettings.AsNoTracking()
            .SingleOrDefaultAsync(value => value.HostId == hostId, cancellationToken);
        return settings is null ? AutomaticRaidShoutoutConfiguration.Defaults : Map(settings);
    }

    public async Task<AutomaticRaidShoutoutSaveOutcome> SaveAsync(
        int hostId,
        AutomaticRaidShoutoutConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = Validate(configuration);
        if (errors.Count > 0)
        {
            return new AutomaticRaidShoutoutSaveOutcome.Invalid(errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Hosts.AnyAsync(host => host.Id == hostId, cancellationToken))
        {
            return new AutomaticRaidShoutoutSaveOutcome.HostNotFound();
        }
        var settings = await db.AutomaticRaidShoutoutSettings.SingleOrDefaultAsync(
            value => value.HostId == hostId,
            cancellationToken
        );
        if (settings is null)
        {
            settings = new AutomaticRaidShoutoutSettings { HostId = hostId };
            db.AutomaticRaidShoutoutSettings.Add(settings);
        }
        settings.Enabled = configuration.Enabled;
        settings.MinimumViewerCount = configuration.MinimumViewerCount;
        settings.Mechanism = configuration.Mechanism;
        settings.ChatPresentation = configuration.ChatPresentation;
        settings.MessageTemplate = configuration.MessageTemplate;
        settings.PinDurationSeconds = configuration.PinDurationSeconds;
        settings.AnnouncementColor = configuration.AnnouncementColor;
        settings.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return new AutomaticRaidShoutoutSaveOutcome.Saved(Map(settings));
    }

    public async Task<AutomaticRaidShoutoutPreviewOutcome> PreviewAsync(
        int hostId,
        AutomaticRaidTemplateValues values,
        CancellationToken cancellationToken
    )
    {
        var configuration = await LoadAsync(hostId, cancellationToken);
        if (configuration is null)
        {
            return new AutomaticRaidShoutoutPreviewOutcome.HostNotFound();
        }
        var parsed = AutomaticRaidShoutoutTemplate.Parse(configuration.MessageTemplate);
        if (parsed is AutomaticRaidTemplateParseOutcome.Invalid invalid)
        {
            return new AutomaticRaidShoutoutPreviewOutcome.InvalidTemplate(invalid.Message);
        }
        var rendered = ((AutomaticRaidTemplateParseOutcome.Valid)parsed).Template.Render(values);
        return rendered switch
        {
            AutomaticRaidTemplateRenderOutcome.Rendered value =>
                new AutomaticRaidShoutoutPreviewOutcome.Rendered(value.Message),
            AutomaticRaidTemplateRenderOutcome.TooLong value =>
                new AutomaticRaidShoutoutPreviewOutcome.TooLong(
                    value.ActualCharacters,
                    value.MaximumCharacters
                ),
            _ => throw new InvalidOperationException("Unsupported template rendering outcome."),
        };
    }

    public async Task<IReadOnlyList<AutomaticRaidShoutoutOutcomeView>> LoadOutcomesAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .AutomaticRaidShoutoutOutcomes.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.MessageTimestampUtc)
            .ThenByDescending(value => value.Id)
            .Select(value => new AutomaticRaidShoutoutOutcomeView(
                value.Id,
                value.ProviderMessageId,
                value.SourceLogin,
                value.SourceDisplayName,
                value.ViewerCount,
                value.Status,
                value.ResultCode,
                new DateTimeOffset(value.MessageTimestampUtc, TimeSpan.Zero),
                value.CompletedAtUtc == null
                    ? null
                    : new DateTimeOffset(value.CompletedAtUtc.Value, TimeSpan.Zero)
            ))
            .ToArrayAsync(cancellationToken);
    }

    internal static AutomaticRaidShoutoutConfiguration Map(AutomaticRaidShoutoutSettings settings)
    {
        return new(
            settings.Enabled,
            settings.MinimumViewerCount,
            settings.Mechanism,
            settings.ChatPresentation,
            settings.MessageTemplate,
            settings.PinDurationSeconds,
            settings.AnnouncementColor
        );
    }

    internal static IReadOnlyList<AutomaticRaidShoutoutValidationError> Validate(
        AutomaticRaidShoutoutConfiguration configuration
    )
    {
        var errors = new List<AutomaticRaidShoutoutValidationError>();
        if (configuration.MinimumViewerCount < 1)
        {
            Add(
                AutomaticRaidShoutoutValidationField.MinimumViewerCount,
                "Minimum viewers must be at least 1."
            );
        }
        if (!Enum.IsDefined(configuration.Mechanism))
        {
            Add(AutomaticRaidShoutoutValidationField.Mechanism, "Choose Native or chat delivery.");
        }
        if (!Enum.IsDefined(configuration.ChatPresentation))
        {
            Add(
                AutomaticRaidShoutoutValidationField.ChatPresentation,
                "Choose a supported chat presentation."
            );
        }
        if (!Enum.IsDefined(configuration.AnnouncementColor))
        {
            Add(
                AutomaticRaidShoutoutValidationField.AnnouncementColor,
                "Choose a supported Twitch announcement color."
            );
        }
        if (configuration.PinDurationSeconds is { } duration && duration is < 30 or > 1800)
        {
            Add(
                AutomaticRaidShoutoutValidationField.PinDuration,
                "Pin duration must be 30 through 1800 seconds, or until stream end."
            );
        }
        if (
            AutomaticRaidShoutoutTemplate.Parse(configuration.MessageTemplate)
            is AutomaticRaidTemplateParseOutcome.Invalid invalid
        )
        {
            Add(AutomaticRaidShoutoutValidationField.MessageTemplate, invalid.Message);
        }
        return errors;

        void Add(AutomaticRaidShoutoutValidationField field, string message)
        {
            errors.Add(new AutomaticRaidShoutoutValidationError(field, message));
        }
    }
}
