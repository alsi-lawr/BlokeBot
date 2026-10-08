using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

internal static class CustomValueIdentity
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Target(string viewerId, string key) =>
        Hash($"{viewerId.Length}:{viewerId}{key}");

    public static bool Valid(string value) => !string.IsNullOrWhiteSpace(value);

    public static bool TryNumber(string value, out long number) =>
        long.TryParse(
            value.Trim(),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out number
        );
}

internal sealed class CustomStoredValueSession(
    IReadOnlyList<CustomValueDefinition> definitions,
    List<CustomStoredValue> values,
    string viewerId,
    bool invocationAvailable,
    BlokeBotDbContext? db = null
)
{
    public static async Task<CustomStoredValueSession> LoadAsync(
        BlokeBotDbContext db,
        int hostId,
        string viewerId,
        bool invocationAvailable,
        CancellationToken ct
    ) =>
        new(
            await db.CustomValueDefinitions.Where(x => x.HostId == hostId).ToListAsync(ct),
            await db
                .CustomStoredValues.Where(x =>
                    x.HostId == hostId
                    && (
                        x.ViewerId == string.Empty
                        || (viewerId != string.Empty && x.ViewerId == viewerId)
                    )
                )
                .ToListAsync(ct),
            viewerId,
            invocationAvailable,
            db
        );

    public CustomStoredValueSession Sandbox() =>
        new(
            definitions,
            values
                .Select(x => new CustomStoredValue
                {
                    Id = x.Id,
                    HostId = x.HostId,
                    DefinitionId = x.DefinitionId,
                    ViewerId = x.ViewerId,
                    EntryKey = x.EntryKey,
                    TargetHash = x.TargetHash,
                    Kind = x.Kind,
                    Number = x.Number,
                    Text = x.Text,
                    IsDefault = x.IsDefault,
                    Revision = x.Revision,
                })
                .ToList(),
            viewerId.Length == 0 ? "preview-viewer" : viewerId,
            true
        );

    public Result<string, TemplateRenderFailure> Apply(
        string operation,
        IReadOnlyList<string> operands
    )
    {
        var dictionary = operation.StartsWith("dict_", StringComparison.OrdinalIgnoreCase);
        var get = operation.EndsWith("_get", StringComparison.OrdinalIgnoreCase);
        var increment = operation.EndsWith("_inc", StringComparison.OrdinalIgnoreCase);
        if (operands.Count != (dictionary ? (get ? 3 : 4) : (get ? 2 : 3)))
        {
            return Failure(TemplateRenderFailure.StoredSyntax);
        }
        var scope = operands[0].ToLowerInvariant() switch
        {
            "user" => CustomValueScope.User,
            "global" => CustomValueScope.Global,
            _ => (CustomValueScope?)null,
        };
        if (scope is null || !CustomValueIdentity.Valid(operands[1]))
        {
            return Failure(TemplateRenderFailure.StoredSyntax);
        }
        if (scope == CustomValueScope.User && !CustomValueIdentity.Valid(viewerId))
        {
            return Failure(TemplateRenderFailure.ViewerIdentity);
        }
        if (!get && !invocationAvailable)
        {
            return Failure(TemplateRenderFailure.InvocationIdentity);
        }
        var definition = definitions.SingleOrDefault(x =>
            x.Scope == scope && x.Name == operands[1]
        );
        if (definition is null || dictionary != (definition.Kind == CustomValueKind.Dictionary))
        {
            return Failure(TemplateRenderFailure.StoredReference);
        }
        var key = dictionary ? operands[2] : string.Empty;
        if (dictionary && !CustomValueIdentity.Valid(key))
        {
            return Failure(TemplateRenderFailure.StoredKey);
        }
        var owner = scope == CustomValueScope.User ? viewerId : string.Empty;
        var stored = values.SingleOrDefault(x =>
            x.DefinitionId == definition.Id && x.ViewerId == owner && x.EntryKey == key
        );
        var exists = stored is { IsDefault: false };
        var kind =
            exists ? stored!.Kind
            : dictionary ? (increment ? CustomValueKind.Number : CustomValueKind.Text)
            : definition.Kind;
        var number =
            exists ? stored!.Number
            : dictionary ? 0
            : definition.DefaultNumber;
        var text = exists ? stored!.Text : definition.DefaultText;
        if (get)
        {
            return Success(
                !exists && dictionary ? definition.DefaultText : Format(kind, number, text)
            );
        }
        var input = operands[^1];
        if (increment)
        {
            if (
                kind != CustomValueKind.Number
                || !CustomValueIdentity.TryNumber(input, out var amount)
            )
            {
                return Failure(TemplateRenderFailure.StoredNumber);
            }
            if (
                (amount > 0 && number > long.MaxValue - amount)
                || (amount < 0 && number < long.MinValue - amount)
            )
            {
                return Failure(TemplateRenderFailure.StoredOverflow);
            }
            number += amount;
        }
        else if (kind == CustomValueKind.Number)
        {
            if (!CustomValueIdentity.TryNumber(input, out number))
            {
                return Failure(TemplateRenderFailure.StoredNumber);
            }
        }
        else
        {
            text = input;
        }
        if (stored is null)
        {
            stored = new()
            {
                HostId = definition.HostId,
                DefinitionId = definition.Id,
                ViewerId = owner,
                EntryKey = key,
                TargetHash = CustomValueIdentity.Target(owner, key),
            };
            values.Add(stored);
            _ = db?.CustomStoredValues.Add(stored);
        }
        stored.Kind = kind;
        stored.Number = number;
        stored.Text = text;
        stored.IsDefault = false;
        stored.Revision = Guid.NewGuid();
        return Success(Format(kind, number, text));
    }

    internal static string Format(CustomValueKind kind, long number, string text) =>
        kind == CustomValueKind.Number ? number.ToString(CultureInfo.InvariantCulture) : text;

    private static Result<string, TemplateRenderFailure> Success(string value) =>
        Result<string, TemplateRenderFailure>.Success(value);

    private static Result<string, TemplateRenderFailure> Failure(TemplateRenderFailure failure) =>
        Result<string, TemplateRenderFailure>.Error(failure);
}
