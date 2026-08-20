using System.Data;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public sealed partial class GuessingConfigurationService
{
    private async ValueTask<
        Result<GuessingProfileCreated, GuessingProfileCreateFailure>
    > ExecuteCreateProfileAsync(
        int hostId,
        GuessingProfileCreateCommand command,
        CancellationToken ct
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        if (await db.Profiles.AnyAsync(x => x.HostId == hostId && x.Slug == command.Slug, ct))
        {
            return Result<GuessingProfileCreated, GuessingProfileCreateFailure>.Error(
                new GuessingProfileCreateFailure()
            );
        }

        var profile = new GuessRoundProfile
        {
            Name = command.Name,
            Slug = command.Slug,
            HostId = hostId,
            IsDefault = !await db.Profiles.AnyAsync(x => x.HostId == hostId, ct),
            ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
        };
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _changes.NotifyChangedAsync(hostId, ct);
        return Result<GuessingProfileCreated, GuessingProfileCreateFailure>.Success(
            new(profile.Id, $"Created {profile.Name}.")
        );
    }

    private async ValueTask<
        Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>
    > ExecuteDeleteProfileAsync(
        int hostId,
        GuessingProfileDeleteCommand command,
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
            return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Error(
                exists
                    ? new GuessingProfileDeleteFailure.ConcurrentEdit()
                    : new GuessingProfileDeleteFailure.ProfileNotFound()
            );
        }

        var profile = await db.Profiles.SingleAsync(
            x => x.Id == command.ProfileId && x.HostId == hostId,
            ct
        );
        if (await db.Profiles.CountAsync(x => x.HostId == hostId, ct) <= 1)
        {
            return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Error(
                new GuessingProfileDeleteFailure.LastProfile()
            );
        }

        if (await db.Rounds.AnyAsync(x => x.GuessRoundProfileId == profile.Id, ct))
        {
            return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Error(
                new GuessingProfileDeleteFailure.UsedByPastRound()
            );
        }

        if (profile.IsDefault)
        {
            profile.IsDefault = false;
            _ = await db.SaveChangesAsync(ct);
            var nextDefault = await db
                .Profiles.Where(x => x.HostId == hostId && x.Id != profile.Id)
                .OrderBy(x => x.Name)
                .FirstAsync(ct);
            nextDefault.IsDefault = true;
            nextDefault.Revision++;
        }

        var deliverySettings = await db
            .ReplyDeliverySettings.Where(x =>
                x.HostId == hostId && x.Feature == ReplyFeature.Guessing && x.ScopeId == profile.Id
            )
            .ToListAsync(ct);
        db.ReplyDeliverySettings.RemoveRange(deliverySettings);
        _ = db.Profiles.Remove(profile);
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _changes.NotifyChangedAsync(hostId, ct);
        return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Success(
            new($"Deleted {profile.Name}.")
        );
    }
}
