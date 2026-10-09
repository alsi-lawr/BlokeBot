using System.Data.Common;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed record CustomValueDefinitionDraft(
    int Id,
    string Name,
    CustomValueScope Scope,
    CustomValueKind Kind,
    string Default,
    Guid Revision
);

internal sealed record CustomValueDeletionPreview(int Count, string Revision);

internal sealed record CustomValueTarget(int DefinitionId, string ViewerId, string Key);

internal sealed record CustomValueSnapshot(
    CustomValueTarget Target,
    CustomValueKind Kind,
    string Value,
    Guid Revision,
    Guid DefinitionRevision,
    bool Saved
);

internal enum CustomValueEditStatus
{
    Saved,
    Invalid,
    Conflict,
    Missing,
    StorageFailure,
}

internal sealed record CustomValueEditOutcome(CustomValueEditStatus Status, string Message)
{
    public static CustomValueEditOutcome Saved { get; } =
        new(CustomValueEditStatus.Saved, "Changes saved.");

    public static CustomValueEditOutcome Invalid(string message) =>
        new(CustomValueEditStatus.Invalid, message);

    public static CustomValueEditOutcome Conflict { get; } =
        new(
            CustomValueEditStatus.Conflict,
            "This value changed since you opened it. Your edit is retained. Reload the current value before saving."
        );
    public static CustomValueEditOutcome Missing { get; } =
        new(
            CustomValueEditStatus.Missing,
            "This definition is no longer available. Reload Variables."
        );
    public static CustomValueEditOutcome StorageFailure { get; } =
        new(
            CustomValueEditStatus.StorageFailure,
            "The save could not be confirmed. Check database storage and reload the current value before retrying."
        );
}

internal sealed partial class CustomStoredValueService(
    IDbContextFactory<BlokeBotDbContext> dbFactory
)
{
    public async Task<IReadOnlyList<CustomValueSnapshot>> ValuesAsync(
        int hostId,
        int definitionId,
        string viewerId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var definition = await db
            .CustomValueDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HostId == hostId && x.Id == definitionId, ct);
        if (
            definition is null
            || (definition.Scope == CustomValueScope.User && !CustomValueIdentity.Valid(viewerId))
        )
        {
            return [];
        }
        var owner = definition.Scope == CustomValueScope.Global ? string.Empty : viewerId;
        var values = await db
            .CustomStoredValues.AsNoTracking()
            .Where(x => x.HostId == hostId && x.DefinitionId == definitionId && x.ViewerId == owner)
            .OrderBy(x => x.EntryKey)
            .ToListAsync(ct);
        return definition.Kind == CustomValueKind.Dictionary
            ? values
                .Where(x => !x.IsDefault)
                .Select(x => Snapshot(definition, x, new(definitionId, owner, x.EntryKey)))
                .ToArray()
            :
            [
                Snapshot(
                    definition,
                    values.SingleOrDefault(x => x.EntryKey == string.Empty),
                    new(definitionId, owner, string.Empty)
                ),
            ];
    }

    public async Task<CustomValueSnapshot?> ValueAsync(
        int hostId,
        CustomValueTarget target,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var definition = await db
            .CustomValueDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HostId == hostId && x.Id == target.DefinitionId, ct);
        if (definition is null || !ValidTarget(definition, target))
        {
            return null;
        }
        var stored = await db
            .CustomStoredValues.AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.DefinitionId == target.DefinitionId
                    && x.TargetHash == CustomValueIdentity.Target(target.ViewerId, target.Key),
                ct
            );
        return Snapshot(definition, stored, target);
    }

    public Task<CustomValueEditOutcome> SaveValueAsync(
        int hostId,
        CustomValueSnapshot original,
        CustomValueKind kind,
        string input,
        CancellationToken ct
    ) => EditValueAsync(hostId, original, kind, input, false, ct);

    public Task<CustomValueEditOutcome> ResetValueAsync(
        int hostId,
        CustomValueSnapshot original,
        CancellationToken ct
    ) => EditValueAsync(hostId, original, original.Kind, string.Empty, true, ct);

    private async Task<CustomValueEditOutcome> EditValueAsync(
        int hostId,
        CustomValueSnapshot original,
        CustomValueKind kind,
        string input,
        bool reset,
        CancellationToken ct
    )
    {
        var number = 0L;
        if (
            kind == CustomValueKind.Dictionary
            || !Enum.IsDefined(kind)
            || (
                !reset
                && kind == CustomValueKind.Number
                && !CustomValueIdentity.TryNumber(input, out number)
            )
        )
        {
            return CustomValueEditOutcome.Invalid("Supply Text or a signed 64-bit whole Number.");
        }
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await MainDatabaseWriteTransaction.StartImmediateAsync(
                db,
                ct
            );
            var target = original.Target;
            var definition = await db.CustomValueDefinitions.SingleOrDefaultAsync(
                x => x.HostId == hostId && x.Id == target.DefinitionId,
                ct
            );
            if (definition is null)
            {
                return CustomValueEditOutcome.Missing;
            }
            if (
                !ValidTarget(definition, target)
                || (
                    !reset
                    && definition.Kind != CustomValueKind.Dictionary
                    && definition.Kind != kind
                )
            )
            {
                return CustomValueEditOutcome.Invalid(
                    "Check the selected scope, viewer, key and value type."
                );
            }
            var hash = CustomValueIdentity.Target(target.ViewerId, target.Key);
            var stored = await db.CustomStoredValues.SingleOrDefaultAsync(
                x => x.DefinitionId == definition.Id && x.TargetHash == hash,
                ct
            );
            if (
                definition.Revision != original.DefinitionRevision
                || (stored?.Revision ?? Guid.Empty) != original.Revision
            )
            {
                return CustomValueEditOutcome.Conflict;
            }
            if (stored is null)
            {
                stored = new()
                {
                    HostId = hostId,
                    DefinitionId = definition.Id,
                    ViewerId = target.ViewerId,
                    EntryKey = target.Key,
                    TargetHash = hash,
                };
                _ = db.CustomStoredValues.Add(stored);
            }
            stored.Kind = kind;
            stored.Number = number;
            stored.Text = reset || kind == CustomValueKind.Number ? string.Empty : input;
            stored.IsDefault = reset;
            stored.Revision = Guid.NewGuid();
            _ = await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return CustomValueEditOutcome.Saved;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return CustomValueEditOutcome.StorageFailure;
        }
    }

    public async Task<CustomStoredValueSession> SandboxAsync(
        int hostId,
        string viewerId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await CustomStoredValueSession.LoadAsync(db, hostId, viewerId, true, ct)).Sandbox();
    }

    private static bool ValidTarget(CustomValueDefinition definition, CustomValueTarget target) =>
        (
            definition.Scope == CustomValueScope.Global
                ? target.ViewerId.Length == 0
                : CustomValueIdentity.Valid(target.ViewerId)
        )
        && (
            definition.Kind == CustomValueKind.Dictionary
                ? CustomValueIdentity.Valid(target.Key)
                : target.Key.Length == 0
        );

    private static CustomValueSnapshot Snapshot(
        CustomValueDefinition definition,
        CustomStoredValue? stored,
        CustomValueTarget target
    ) =>
        new(
            target,
            stored is { IsDefault: false } ? stored.Kind
                : definition.Kind == CustomValueKind.Dictionary ? CustomValueKind.Text
                : definition.Kind,
            stored is { IsDefault: false }
                ? CustomStoredValueSession.Format(stored.Kind, stored.Number, stored.Text)
                : CustomStoredValueSession.Format(
                    definition.Kind,
                    definition.DefaultNumber,
                    definition.DefaultText
                ),
            stored?.Revision ?? Guid.Empty,
            definition.Revision,
            stored is { IsDefault: false }
        );
}
