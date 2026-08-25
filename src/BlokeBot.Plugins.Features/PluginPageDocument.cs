using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed record PluginPageDocument(
    int Version,
    string? Introduction,
    ImmutableArray<PluginPageSection> Sections
);

public abstract record PluginPageSection
{
    private PluginPageSection() { }

    public sealed record Text(string Title, string Body) : PluginPageSection;

    public sealed record Status(string Title, string? Description, PluginPageStatusTone Tone)
        : PluginPageSection;

    public sealed record Form(
        string Title,
        string? Description,
        PluginActionId Action,
        string SubmitLabel,
        ImmutableArray<PluginPageField> Fields
    ) : PluginPageSection;

    public sealed record Table(
        string Title,
        string? Description,
        ImmutableArray<PluginPageTableColumn> Columns,
        ImmutableArray<PluginPageTableRow> Rows
    ) : PluginPageSection;

    public sealed record List(
        string Title,
        string? Description,
        ImmutableArray<PluginPageListItem> Items
    ) : PluginPageSection;
}

public enum PluginPageStatusTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

public enum PluginPageFieldKind
{
    Text,
    Multiline,
    Number,
    Boolean,
    Choice,
}

public sealed record PluginPageField(
    string Id,
    string Label,
    PluginPageFieldKind Kind,
    bool Required,
    string? Help,
    ImmutableArray<PluginPageChoice> Choices
);

public sealed record PluginPageChoice(string Value, string Label);

public sealed record PluginPageTableColumn(string Id, string Label);

public sealed record PluginPageTableRow(ImmutableDictionary<string, string> Cells);

public sealed record PluginPageListItem(string Title, string? Description, string? Status);

public enum PluginPageDocumentErrorCode
{
    InvalidRoot,
    UnsupportedVersion,
    InvalidSchema,
    LimitExceeded,
    UnknownAction,
}

public sealed record PluginPageDocumentError(PluginPageDocumentErrorCode Code, string Location);

public abstract record PluginPageDocumentParseOutcome
{
    private PluginPageDocumentParseOutcome() { }

    public sealed record Parsed(PluginPageDocument Document) : PluginPageDocumentParseOutcome;

    public sealed record Rejected(IReadOnlyList<PluginPageDocumentError> Errors)
        : PluginPageDocumentParseOutcome;
}
