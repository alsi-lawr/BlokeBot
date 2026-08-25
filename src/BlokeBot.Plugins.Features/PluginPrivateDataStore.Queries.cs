using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using Microsoft.Data.Sqlite;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginPrivateDataStore
{
    private static async ValueTask<PluginSqliteOutcome> QueryAsync(
        SqliteCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (reader.FieldCount > PluginContractLimits.MaximumSqlColumns)
        {
            return Rejected(PluginSqliteRejectionCode.ResultTooLarge);
        }

        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            return Rejected(PluginSqliteRejectionCode.InvalidStatement);
        }

        var rows = ImmutableArray.CreateBuilder<PluginValue.Map>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= PluginContractLimits.MaximumSqlRows)
            {
                return Rejected(PluginSqliteRejectionCode.ResultTooLarge);
            }

            var properties = ImmutableArray.CreateBuilder<PluginValueProperty>(reader.FieldCount);
            for (var column = 0; column < reader.FieldCount; column++)
            {
                if (!TryPluginValue(reader.GetValue(column), out var value))
                {
                    return Rejected(PluginSqliteRejectionCode.ResultTooLarge);
                }
                properties.Add(new(names[column], value));
            }
            rows.Add(new(properties.ToImmutable()));
        }

        var result = new PluginValue.Array(rows.Cast<PluginValue>().ToImmutableArray());
        return PluginValueValidator.Validate(result) is PluginValueValidationOutcome.Valid
            ? new PluginSqliteOutcome.Rows(rows.ToImmutable())
            : Rejected(PluginSqliteRejectionCode.ResultTooLarge);
    }

    private static bool TryPluginValue(object value, out PluginValue result)
    {
        result = value switch
        {
            DBNull => new PluginValue.Nil(),
            long number => new PluginValue.Number(number),
            double number when double.IsFinite(number) => new PluginValue.Number(number),
            string text => new PluginValue.String(text),
            byte[] bytes => new PluginValue.String(Convert.ToBase64String(bytes)),
            _ => null!,
        };
        return result is not null;
    }
}
