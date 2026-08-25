using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public static partial class PluginPageDocumentParser
{
    private static PluginPageSection? ParseTable(
        IReadOnlyDictionary<string, PluginValue> fields,
        string title,
        string? description,
        string location,
        List<PluginPageDocumentError> errors
    )
    {
        if (!Array(fields, "columns", out var columns) || !Array(fields, "rows", out var rows))
        {
            return Invalid<PluginPageSection>(location, errors);
        }
        if (
            columns.Length > PluginContractLimits.MaximumPageTableColumns
            || rows.Length > PluginContractLimits.MaximumPageTableRows
        )
        {
            errors.Add(new(PluginPageDocumentErrorCode.LimitExceeded, location));
        }
        var parsedColumns = ImmutableArray.CreateBuilder<PluginPageTableColumn>();
        foreach (var column in columns)
        {
            if (column is not PluginValue.Map columnMap)
            {
                return Invalid<PluginPageSection>(location, errors);
            }
            var columnFields = Fields(columnMap);
            if (
                !String(columnFields, "id", out var id)
                || !ValidLocalId(id)
                || !String(columnFields, "label", out var label)
            )
            {
                return Invalid<PluginPageSection>(location, errors);
            }
            parsedColumns.Add(new(id, label));
        }
        var columnIds = parsedColumns
            .Select(static column => column.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);
        if (columnIds.Count != parsedColumns.Count)
        {
            return Invalid<PluginPageSection>(location, errors);
        }
        var parsedRows = ImmutableArray.CreateBuilder<PluginPageTableRow>();
        foreach (var row in rows)
        {
            if (row is not PluginValue.Map rowMap)
            {
                return Invalid<PluginPageSection>(location, errors);
            }
            var cells = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var cell in rowMap.Properties)
            {
                if (!columnIds.Contains(cell.Name) || cell.Value is not PluginValue.String text)
                {
                    return Invalid<PluginPageSection>(location, errors);
                }
                cells[cell.Name] = text.Value;
            }
            parsedRows.Add(new(cells.ToImmutable()));
        }
        return new PluginPageSection.Table(
            title,
            description,
            parsedColumns.ToImmutable(),
            parsedRows.ToImmutable()
        );
    }

    private static PluginPageSection? ParseList(
        IReadOnlyDictionary<string, PluginValue> fields,
        string title,
        string? description,
        string location,
        List<PluginPageDocumentError> errors
    )
    {
        if (!Array(fields, "items", out var values))
        {
            return Invalid<PluginPageSection>(location, errors);
        }
        if (values.Length > PluginContractLimits.MaximumPageListItems)
        {
            errors.Add(new(PluginPageDocumentErrorCode.LimitExceeded, $"{location}.items"));
        }
        var items = ImmutableArray.CreateBuilder<PluginPageListItem>();
        foreach (var value in values)
        {
            if (value is not PluginValue.Map map)
            {
                return Invalid<PluginPageSection>(location, errors);
            }
            var itemFields = Fields(map);
            if (!String(itemFields, "title", out var itemTitle))
            {
                return Invalid<PluginPageSection>(location, errors);
            }
            if (
                !OptionalString(itemFields, "description", out var itemDescription)
                || !OptionalString(itemFields, "status", out var status)
            )
            {
                return Invalid<PluginPageSection>(location, errors);
            }
            items.Add(new(itemTitle, itemDescription, status));
        }
        return new PluginPageSection.List(title, description, items.ToImmutable());
    }
}
