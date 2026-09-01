using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BlokeBot.DatabaseCutover;

internal sealed record CutoverColumn(string Name, string TargetStoreType);

internal sealed record CutoverIdentity(string Column, string TargetStoreType);

internal sealed record CutoverTable(
    string Name,
    IReadOnlyList<CutoverColumn> Columns,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<CutoverIdentity> Identities
);

internal static class CutoverCatalog
{
    private const string _resourceName = "BlokeBot.DatabaseCutover.Catalog.domain-tables.txt";

    internal static IReadOnlyList<CutoverTable> Load(
        BlokeBotDbContext source,
        BlokeBotDbContext target
    )
    {
        var reviewedOrder = ReadReviewedOrder();
        var sourceModel = ModelTables(source);
        var targetModel = ModelTables(target);
        var calculatedOrder = DependencyOrder(target.Model);

        EnsureSame(reviewedOrder, calculatedOrder, "reviewed domain table order");
        EnsureSame(
            reviewedOrder.Order(StringComparer.Ordinal),
            sourceModel.Keys.Order(StringComparer.Ordinal),
            "SQLite domain table catalog"
        );
        EnsureSame(
            reviewedOrder.Order(StringComparer.Ordinal),
            targetModel.Keys.Order(StringComparer.Ordinal),
            "PostgreSql domain table catalog"
        );

        return reviewedOrder
            .Select(name => CreateTable(name, sourceModel[name], targetModel[name]))
            .ToArray();
    }

    private static CutoverTable CreateTable(string name, ITable source, ITable target)
    {
        var sourceColumns = source.Columns.Select(column => column.Name).ToArray();
        var targetColumns = target.Columns.Select(column => column.Name).ToArray();
        EnsureSame(sourceColumns, targetColumns, $"columns for {name}");

        var sourceKey = source.PrimaryKey?.Columns.Select(column => column.Name).ToArray() ?? [];
        var targetKey = target.PrimaryKey?.Columns.Select(column => column.Name).ToArray() ?? [];
        EnsureSame(sourceKey, targetKey, $"primary key for {name}");
        if (targetKey.Length == 0)
        {
            throw new InvalidOperationException($"Domain table {name} has no primary key.");
        }

        var identities = target
            .Columns.Where(IsGeneratedNumericIdentity)
            .Select(column => new CutoverIdentity(column.Name, column.StoreType))
            .ToArray();
        return new CutoverTable(
            name,
            target
                .Columns.Select(column => new CutoverColumn(column.Name, column.StoreType))
                .ToArray(),
            targetKey,
            identities
        );
    }

    private static bool IsGeneratedNumericIdentity(IColumn column) =>
        column.PropertyMappings.Any(mapping =>
            mapping.Property.ValueGenerated == ValueGenerated.OnAdd
            && Type.GetTypeCode(
                Nullable.GetUnderlyingType(mapping.Property.ClrType) ?? mapping.Property.ClrType
            )
                is TypeCode.Byte
                    or TypeCode.Int16
                    or TypeCode.Int32
                    or TypeCode.Int64
                    or TypeCode.UInt16
                    or TypeCode.UInt32
                    or TypeCode.UInt64
        );

    private static Dictionary<string, ITable> ModelTables(BlokeBotDbContext db) =>
        db
            .Model.GetRelationalModel()
            .Tables.ToDictionary(table => table.Name, StringComparer.Ordinal);

    private static IReadOnlyList<string> DependencyOrder(IModel model)
    {
        var names = model
            .GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var dependencies = names.ToDictionary(
            name => name,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal
        );
        foreach (var entity in model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var principal = foreignKey.PrincipalEntityType.GetTableName();
                if (principal is not null && !StringComparer.Ordinal.Equals(table, principal))
                {
                    _ = dependencies[table].Add(principal);
                }
            }
        }

        var remaining = names.ToHashSet(StringComparer.Ordinal);
        var ordered = new List<string>(names.Length);
        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(name =>
                    dependencies[name].All(dependency => !remaining.Contains(dependency))
                )
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
            {
                throw new InvalidOperationException(
                    "The domain table dependency graph contains a cycle."
                );
            }

            foreach (var name in ready)
            {
                _ = remaining.Remove(name);
                ordered.Add(name);
            }
        }

        return ordered;
    }

    private static string[] ReadReviewedOrder()
    {
        using var stream =
            typeof(CutoverCatalog).Assembly.GetManifestResourceStream(_resourceName)
            ?? throw new InvalidOperationException("The reviewed domain table catalog is missing.");
        using var reader = new StreamReader(stream);
        return reader
            .ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .ToArray();
    }

    private static void EnsureSame(
        IEnumerable<string> expected,
        IEnumerable<string> actual,
        string subject
    )
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The {subject} has changed. Review the cutover catalog."
            );
        }
    }
}
