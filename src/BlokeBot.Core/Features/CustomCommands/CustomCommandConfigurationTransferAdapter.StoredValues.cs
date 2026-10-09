using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter
{
    private static async Task<
        IReadOnlyList<ConfigurationValidationIssue>
    > StageStoredDefinitionsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<StoredDefinitionV1>? definitions,
        ImportConflictStrategy strategy,
        CancellationToken ct
    )
    {
        if (definitions is null)
        {
            return [];
        }
        var existing = await db
            .CustomValueDefinitions.Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        foreach (var imported in definitions)
        {
            if (
                !CustomStoredValueService.ValidDefinition(
                    new(
                        0,
                        imported.Name,
                        imported.Scope,
                        imported.Kind,
                        imported.Default,
                        Guid.Empty
                    ),
                    out var number
                )
            )
            {
                return
                [
                    new(
                        "sections.customCommands.storedDefinitions",
                        "Check stored-value names, scopes, types and defaults."
                    ),
                ];
            }
            var hash = CustomValueIdentity.Hash(imported.Name);
            var matched = existing.SingleOrDefault(x =>
                x.Scope == imported.Scope
                && x.NameHash == hash
                && CustomValueIdentity.SameNamespace(x.Kind, imported.Kind)
            );
            if (matched is not null && strategy == ImportConflictStrategy.AddMissing)
            {
                continue;
            }
            if (
                matched is not null
                && (matched.Kind != imported.Kind || matched.Name != imported.Name)
            )
            {
                return
                [
                    new(
                        "sections.customCommands.storedDefinitions",
                        "A stored-value definition has a different type. Explicitly delete the destination definition and its data in Variables before importing the new type."
                    ),
                ];
            }
            if (matched is null)
            {
                matched = new()
                {
                    HostId = hostId,
                    Name = imported.Name,
                    NameHash = hash,
                    Scope = imported.Scope,
                    Kind = imported.Kind,
                };
                _ = db.CustomValueDefinitions.Add(matched);
                existing.Add(matched);
            }
            matched.DefaultNumber = number;
            matched.DefaultText =
                imported.Kind == CustomValueKind.Number ? string.Empty : imported.Default;
            matched.Revision = Guid.NewGuid();
        }
        return [];
    }
}
