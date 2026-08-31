using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public abstract record PluginLuaFieldShape
{
    private PluginLuaFieldShape(string luaTypeName) => LuaTypeName = luaTypeName;

    public string LuaTypeName { get; }

    internal abstract bool Accepts(PluginValue value);

    public sealed record BooleanValue : PluginLuaFieldShape
    {
        internal BooleanValue()
            : base("boolean") { }

        internal override bool Accepts(PluginValue value) => value is PluginValue.Boolean;
    }

    public sealed record NumberValue : PluginLuaFieldShape
    {
        internal NumberValue()
            : base("number") { }

        internal override bool Accepts(PluginValue value) => value is PluginValue.Number;
    }

    public sealed record Text : PluginLuaFieldShape
    {
        internal Text()
            : base("string") { }

        internal override bool Accepts(PluginValue value) => value is PluginValue.String;
    }

    public sealed record Integer : PluginLuaFieldShape
    {
        internal Integer()
            : base("integer") { }

        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Number number && number.Value == Math.Truncate(number.Value);
    }

    public sealed record TextArray : PluginLuaFieldShape
    {
        internal TextArray()
            : base("string[]") { }

        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Array array
            && array.Items.All(static item => item is PluginValue.String);
    }

    public sealed record ValueArray : PluginLuaFieldShape
    {
        internal ValueArray()
            : base("BlokeBotValue[]") { }

        internal override bool Accepts(PluginValue value) => value is PluginValue.Array;
    }

    public sealed record TextMap : PluginLuaFieldShape
    {
        internal TextMap()
            : base("table<string, string>") { }

        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Map map
            && map.Properties.All(static property => property.Value is PluginValue.String);
    }

    public sealed record ValueMap : PluginLuaFieldShape
    {
        internal ValueMap()
            : base("table<string, BlokeBotValue>") { }

        internal override bool Accepts(PluginValue value) => value is PluginValue.Map;
    }

    public sealed record LiteralText : PluginLuaFieldShape
    {
        internal LiteralText(params ReadOnlySpan<string> values)
            : base(string.Join('|', values.ToArray().Select(static value => $"\"{value}\"")))
        {
            if (values.IsEmpty || values.ToArray().Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "A literal string shape requires values.",
                    nameof(values)
                );
            }
            Values = [.. values];
        }

        public ImmutableArray<string> Values { get; }

        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.String text && Values.Contains(text.Value, StringComparer.Ordinal);
    }

    public sealed record Structured(PluginLuaSchemaDescriptor Schema)
        : PluginLuaFieldShape(Schema.LuaTypeName)
    {
        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Map map && Schema.Accepts(map);
    }

    public static PluginLuaFieldShape Boolean { get; } = new BooleanValue();

    public static PluginLuaFieldShape Number { get; } = new NumberValue();

    public static PluginLuaFieldShape String { get; } = new Text();

    public static PluginLuaFieldShape WholeNumber { get; } = new Integer();

    public static PluginLuaFieldShape StringArray { get; } = new TextArray();

    public static PluginLuaFieldShape Array { get; } = new ValueArray();

    public static PluginLuaFieldShape StringMap { get; } = new TextMap();

    public static PluginLuaFieldShape Map { get; } = new ValueMap();

    public static PluginLuaFieldShape For(PluginValueKind kind) =>
        kind switch
        {
            PluginValueKind.Nil => throw new ArgumentOutOfRangeException(nameof(kind)),
            PluginValueKind.Boolean => Boolean,
            PluginValueKind.Number => Number,
            PluginValueKind.String => String,
            PluginValueKind.Array => Array,
            PluginValueKind.Map => Map,
        };
}

public sealed record PluginLuaFieldDescriptor
{
    public PluginLuaFieldDescriptor(
        string name,
        PluginLuaFieldShape shape,
        string description,
        bool required = true
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Name = name;
        Shape = shape;
        Description = description;
        Required = required;
    }

    public string Name { get; }

    public PluginLuaFieldShape Shape { get; }

    public string Description { get; }

    public bool Required { get; }

    public PluginLuaFieldValue Value(PluginValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Shape.Accepts(value) || (!Required && value is PluginValue.Nil)
            ? new(this, value)
            : throw new ArgumentException(
                $"Lua schema field '{Name}' requires {Shape.LuaTypeName}.",
                nameof(value)
            );
    }
}

public sealed record PluginLuaFieldValue(PluginLuaFieldDescriptor Field, PluginValue Value);

public sealed record PluginLuaSchemaDescriptor
{
    public PluginLuaSchemaDescriptor(
        string luaTypeName,
        string description,
        ImmutableArray<PluginLuaFieldDescriptor> fields,
        PluginLuaSchemaDescriptor? baseSchema = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(luaTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (fields.IsDefault)
        {
            throw new ArgumentException("Lua schema fields must be initialized.", nameof(fields));
        }
        var allFields = baseSchema is null ? fields : [.. baseSchema.AllFields, .. fields];
        if (
            allFields.Select(static field => field.Name).Distinct(StringComparer.Ordinal).Count()
            != allFields.Length
        )
        {
            throw new ArgumentException("Lua schema field names must be unique.", nameof(fields));
        }
        LuaTypeName = luaTypeName;
        Description = description;
        Fields = fields;
        BaseSchema = baseSchema;
        AllFields = allFields;
    }

    public string LuaTypeName { get; }

    public string Description { get; }

    public ImmutableArray<PluginLuaFieldDescriptor> Fields { get; }

    public PluginLuaSchemaDescriptor? BaseSchema { get; }

    public ImmutableArray<PluginLuaFieldDescriptor> AllFields { get; }

    public PluginValue.Map Create(params ReadOnlySpan<PluginLuaFieldValue> values)
    {
        var supplied = new Dictionary<string, PluginLuaFieldValue>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!AllFields.Contains(value.Field) || !supplied.TryAdd(value.Field.Name, value))
            {
                throw new ArgumentException(
                    $"Lua schema '{LuaTypeName}' received an unknown or duplicate field.",
                    nameof(values)
                );
            }
        }
        return AllFields.Any(field => field.Required && !supplied.ContainsKey(field.Name))
            ? throw new ArgumentException(
                $"Lua schema '{LuaTypeName}' requires every non-optional field.",
                nameof(values)
            )
            : new([
                .. AllFields
                    .Where(field => supplied.ContainsKey(field.Name))
                    .Select(field => new PluginValueProperty(
                        field.Name,
                        supplied[field.Name].Value
                    )),
            ]);
    }

    public bool Accepts(PluginValue.Map map)
    {
        if (
            map.Properties.Select(static property => property.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != map.Properties.Length
        )
        {
            return false;
        }
        var properties = map.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value,
            StringComparer.Ordinal
        );
        return properties.Keys.All(name => AllFields.Any(field => field.Name == name))
            && AllFields.All(field =>
                properties.TryGetValue(field.Name, out var value)
                    ? field.Shape.Accepts(value) || (!field.Required && value is PluginValue.Nil)
                    : !field.Required
            );
    }
}

public sealed record PluginLuaUnionDescriptor
{
    public PluginLuaUnionDescriptor(
        string luaTypeName,
        string description,
        ImmutableArray<PluginLuaSchemaDescriptor> alternatives
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(luaTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (alternatives.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A Lua union requires alternatives.", nameof(alternatives));
        }
        LuaTypeName = luaTypeName;
        Description = description;
        Alternatives = alternatives;
    }

    public string LuaTypeName { get; }

    public string Description { get; }

    public ImmutableArray<PluginLuaSchemaDescriptor> Alternatives { get; }
}
