using System.Data;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public sealed partial class GuessingConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes
)
{
    private readonly IDbContextFactory<BlokeBotDbContext> _dbFactory = dbFactory;
    private readonly GuessingChangeNotifier _changes = changes;

    public IO<GuessingProfileCreated, GuessingProfileCreateFailure> CreateProfile(
        int hostId,
        GuessingProfileCreateCommand command
    ) =>
        IO<GuessingProfileCreated, GuessingProfileCreateFailure>.Create(ct =>
            ExecuteCreateProfileAsync(hostId, command, ct)
        );

    public IO<GuessingProfileDeleted, GuessingProfileDeleteFailure> DeleteProfile(
        int hostId,
        GuessingProfileDeleteCommand command
    ) =>
        IO<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Create(ct =>
            ExecuteDeleteProfileAsync(hostId, command, ct)
        );

    public IO<GuessingConfiguration, GuessingConfigurationLoadFailure> LoadConfiguration(
        int hostId,
        GuessingProfileSelection selection
    ) =>
        IO<GuessingConfiguration, GuessingConfigurationLoadFailure>.Create(ct =>
            ExecuteLoadConfigurationAsync(hostId, selection, ct)
        );

    public IO<GuessingConfigurationSaved, GuessingConfigurationSaveFailure> SaveConfiguration(
        int hostId,
        GuessingConfigurationSaveCommand command
    ) =>
        IO<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Create(ct =>
            ExecuteSaveConfigurationAsync(hostId, command, ct)
        );

    private async ValueTask<
        Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>
    > ExecuteSaveConfigurationAsync(
        int hostId,
        GuessingConfigurationSaveCommand command,
        CancellationToken ct
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        var claimed = await db
            .Profiles.Where(profile =>
                profile.HostId == hostId
                && profile.Id == command.ProfileId
                && profile.Revision == command.ExpectedRevision
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        profile => profile.Revision,
                        profile => profile.Revision + 1
                    ),
                ct
            );
        if (claimed == 0)
        {
            var exists = await db.Profiles.AnyAsync(
                profile => profile.HostId == hostId && profile.Id == command.ProfileId,
                ct
            );
            return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Error(
                exists
                    ? new GuessingConfigurationSaveFailure.ConcurrentEdit()
                    : new GuessingConfigurationSaveFailure.ProfileNotFound()
            );
        }

        var slug = GuessRoundProfileSlug.FromName(command.ProfileName).Value;
        if (
            await db.Profiles.AnyAsync(
                profile =>
                    profile.HostId == hostId
                    && profile.Id != command.ProfileId
                    && profile.Slug == slug,
                ct
            )
        )
        {
            return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Error(
                new GuessingConfigurationSaveFailure.DuplicateProfileName()
            );
        }

        var aliasFailure = await GuessingConfigurationGraphStager.FindAliasFailureAsync(
            db,
            hostId,
            command,
            ct
        );
        if (aliasFailure is not null)
        {
            return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Error(
                aliasFailure
            );
        }

        if (command.IsDefault)
        {
            _ = await db
                .Profiles.Where(profile =>
                    profile.HostId == hostId && profile.Id != command.ProfileId && profile.IsDefault
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(profile => profile.IsDefault, false)
                            .SetProperty(
                                profile => profile.Revision,
                                profile => profile.Revision + 1
                            ),
                    ct
                );
        }

        var profile = await db
            .Profiles.Include(x => x.ReplySettings)
            .Include(x => x.Options)
            .SingleAsync(x => x.Id == command.ProfileId && x.HostId == hostId, ct);
        GuessingConfigurationGraphStager.ApplyProfile(db, hostId, profile, command);
        await ReplyDeliverySettingWriter.ReplaceAsync(
            db,
            hostId,
            ReplyFeature.Guessing,
            profile.Id,
            command.ReplyDelivery.Only(GuessingReplyKeys.WhisperableKeys),
            ct
        );
        var pinPolicy = await db.ReplyPinPolicies.SingleOrDefaultAsync(
            policy =>
                policy.HostId == hostId
                && policy.Feature == "guessing"
                && policy.ReplyKey == GuessingReplyKeys.RoundStarted,
            ct
        );
        if (!command.Pin.Enabled && pinPolicy is not null)
        {
            _ = db.ReplyPinPolicies.Remove(pinPolicy);
        }
        else if (command.Pin.Enabled)
        {
            pinPolicy ??= new ReplyPinPolicy
            {
                HostId = hostId,
                Feature = "guessing",
                ReplyKey = GuessingReplyKeys.RoundStarted,
            };
            pinPolicy.DurationSeconds = command.Pin.DurationSeconds;
            pinPolicy.UnpinOnOwnerCompletion = command.Pin.UnpinWhenRoundStops;
            if (pinPolicy.Id == 0)
            {
                _ = db.ReplyPinPolicies.Add(pinPolicy);
            }
        }

        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _changes.NotifyChangedAsync(hostId, ct);
        return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Success(new());
    }
}
