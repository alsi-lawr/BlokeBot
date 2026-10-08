using System.Data.Common;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomStoredValueService
{
    public async Task<IReadOnlyList<CustomValueDefinitionDraft>> DefinitionsAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var definitions = await db
            .CustomValueDefinitions.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        return definitions
            .Select(x => new CustomValueDefinitionDraft(
                x.Id,
                x.Name,
                x.Scope,
                x.Kind,
                CustomStoredValueSession.Format(x.Kind, x.DefaultNumber, x.DefaultText),
                x.Revision
            ))
            .ToArray();
    }

    public async Task<CustomValueEditOutcome> SaveDefinitionAsync(
        int hostId,
        CustomValueDefinitionDraft draft,
        CancellationToken ct
    )
    {
        if (!ValidDefinition(draft, out var number))
        {
            return CustomValueEditOutcome.Invalid(
                "Supply a nonempty name, a scope, a value type and a whole-number default for Number values."
            );
        }
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
                db,
                ct
            );
            var definition =
                draft.Id == 0
                    ? null
                    : await db.CustomValueDefinitions.SingleOrDefaultAsync(
                        x => x.HostId == hostId && x.Id == draft.Id,
                        ct
                    );
            if (draft.Id != 0 && definition is null)
            {
                return CustomValueEditOutcome.Missing;
            }
            if (definition is not null && definition.Revision != draft.Revision)
            {
                return CustomValueEditOutcome.Conflict;
            }
            if (
                definition is not null
                && (definition.Kind != draft.Kind || definition.Scope != draft.Scope)
            )
            {
                return CustomValueEditOutcome.Invalid(
                    "Scope and type are fixed. Explicitly delete the definition and its saved values before creating a different definition."
                );
            }
            var hash = CustomValueIdentity.Hash(draft.Name);
            if (
                await db.CustomValueDefinitions.AnyAsync(
                    x =>
                        x.HostId == hostId
                        && x.Scope == draft.Scope
                        && x.NameHash == hash
                        && x.Id != draft.Id,
                    ct
                )
            )
            {
                return CustomValueEditOutcome.Invalid(
                    "This scope already has a definition with that name."
                );
            }
            if (definition is null)
            {
                definition = new()
                {
                    HostId = hostId,
                    Scope = draft.Scope,
                    Kind = draft.Kind,
                };
                _ = db.CustomValueDefinitions.Add(definition);
            }
            definition.Name = draft.Name;
            definition.NameHash = hash;
            definition.DefaultNumber = number;
            definition.DefaultText =
                draft.Kind == CustomValueKind.Number ? string.Empty : draft.Default;
            definition.Revision = Guid.NewGuid();
            _ = await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return CustomValueEditOutcome.Saved;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return CustomValueEditOutcome.StorageFailure;
        }
    }

    internal static bool ValidDefinition(CustomValueDefinitionDraft draft, out long number)
    {
        number = 0;
        return CustomValueIdentity.Valid(draft.Name)
            && Enum.IsDefined(draft.Kind)
            && Enum.IsDefined(draft.Scope)
            && (
                draft.Kind != CustomValueKind.Number
                || CustomValueIdentity.TryNumber(draft.Default, out number)
            );
    }

    public async Task<CustomValueDeletionPreview> AffectedValuesAsync(
        int hostId,
        int definitionId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await DeletionPreviewAsync(db, hostId, definitionId, ct);
    }

    public async Task<CustomValueEditOutcome> DeleteDefinitionAsync(
        int hostId,
        CustomValueDefinitionDraft original,
        CustomValueDeletionPreview confirmed,
        CancellationToken ct
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
                db,
                ct
            );
            var definition = await db.CustomValueDefinitions.SingleOrDefaultAsync(
                x => x.HostId == hostId && x.Id == original.Id,
                ct
            );
            if (definition is null)
            {
                return CustomValueEditOutcome.Missing;
            }
            if (
                definition.Revision != original.Revision
                || await DeletionPreviewAsync(db, hostId, original.Id, ct) != confirmed
            )
            {
                return CustomValueEditOutcome.Conflict;
            }
            _ = db.CustomValueDefinitions.Remove(definition);
            _ = await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return CustomValueEditOutcome.Saved;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return CustomValueEditOutcome.StorageFailure;
        }
    }

    private static async Task<CustomValueDeletionPreview> DeletionPreviewAsync(
        BlokeBotDbContext db,
        int hostId,
        int definitionId,
        CancellationToken ct
    )
    {
        var revisions = await db
            .CustomStoredValues.AsNoTracking()
            .Where(x => x.HostId == hostId && x.DefinitionId == definitionId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Revision,
                x.IsDefault,
            })
            .ToArrayAsync(ct);
        return new(
            revisions.Count(x => !x.IsDefault),
            CustomValueIdentity.Hash(
                string.Join(';', revisions.Select(x => $"{x.Id}:{x.Revision:N}"))
            )
        );
    }
}
