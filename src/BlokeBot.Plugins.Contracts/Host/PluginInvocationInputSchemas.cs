using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public abstract record PluginInvocationInputFieldShape
{
    private PluginInvocationInputFieldShape(string luaTypeName) => LuaTypeName = luaTypeName;

    public string LuaTypeName { get; }

    internal abstract bool Accepts(PluginValue value);

    public sealed record Text : PluginInvocationInputFieldShape
    {
        internal Text()
            : base("string") { }

        internal override bool Accepts(PluginValue value) => value is PluginValue.String;
    }

    public sealed record Integer : PluginInvocationInputFieldShape
    {
        internal Integer()
            : base("integer") { }

        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Number number && number.Value == Math.Truncate(number.Value);
    }

    public sealed record TextArray : PluginInvocationInputFieldShape
    {
        internal TextArray()
            : base("string[]") { }

        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Array array
            && array.Items.All(static item => item is PluginValue.String);
    }

    public sealed record TextMap : PluginInvocationInputFieldShape
    {
        internal TextMap()
            : base("table<string, string>") { }

        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Map map
            && map.Properties.All(static property => property.Value is PluginValue.String);
    }

    public sealed record ValueMap : PluginInvocationInputFieldShape
    {
        internal ValueMap()
            : base("table<string, BlokeBotValue>") { }

        internal override bool Accepts(PluginValue value) => value is PluginValue.Map;
    }

    public sealed record Structured(PluginInvocationInputSchemaDescriptor Schema)
        : PluginInvocationInputFieldShape(Schema.LuaTypeName)
    {
        internal override bool Accepts(PluginValue value) =>
            value is PluginValue.Map map && Schema.Accepts(map);
    }

    public static PluginInvocationInputFieldShape String { get; } = new Text();

    public static PluginInvocationInputFieldShape WholeNumber { get; } = new Integer();

    public static PluginInvocationInputFieldShape StringArray { get; } = new TextArray();

    public static PluginInvocationInputFieldShape StringMap { get; } = new TextMap();

    public static PluginInvocationInputFieldShape Map { get; } = new ValueMap();
}

public sealed record PluginInvocationInputFieldDescriptor
{
    public PluginInvocationInputFieldDescriptor(
        string name,
        PluginInvocationInputFieldShape shape,
        string description
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Name = name;
        Shape = shape;
        Description = description;
    }

    public string Name { get; }

    public PluginInvocationInputFieldShape Shape { get; }

    public string Description { get; }

    public PluginInvocationInputFieldValue Value(PluginValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Shape.Accepts(value)
            ? new(this, value)
            : throw new ArgumentException(
                $"Invocation input field '{Name}' requires {Shape.LuaTypeName}.",
                nameof(value)
            );
    }
}

public sealed record PluginInvocationInputFieldValue(
    PluginInvocationInputFieldDescriptor Field,
    PluginValue Value
);

public sealed record PluginInvocationInputSchemaDescriptor
{
    public PluginInvocationInputSchemaDescriptor(
        string luaTypeName,
        string description,
        ImmutableArray<PluginInvocationInputFieldDescriptor> fields
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(luaTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (fields.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An invocation input schema requires fields.",
                nameof(fields)
            );
        }
        if (
            fields.Select(static field => field.Name).Distinct(StringComparer.Ordinal).Count()
            != fields.Length
        )
        {
            throw new ArgumentException(
                "Invocation input schema field names must be unique.",
                nameof(fields)
            );
        }
        LuaTypeName = luaTypeName;
        Description = description;
        Fields = fields;
    }

    public string LuaTypeName { get; }

    public string Description { get; }

    public ImmutableArray<PluginInvocationInputFieldDescriptor> Fields { get; }

    public PluginValue.Map Create(params ReadOnlySpan<PluginInvocationInputFieldValue> values)
    {
        if (values.Length != Fields.Length)
        {
            throw new ArgumentException(
                $"Invocation input schema '{LuaTypeName}' requires {Fields.Length} fields.",
                nameof(values)
            );
        }

        var properties = ImmutableArray.CreateBuilder<PluginValueProperty>(Fields.Length);
        for (var index = 0; index < Fields.Length; index++)
        {
            if (!ReferenceEquals(values[index].Field, Fields[index]))
            {
                throw new ArgumentException(
                    $"Invocation input schema '{LuaTypeName}' requires field '{Fields[index].Name}' at position {index}.",
                    nameof(values)
                );
            }
            properties.Add(new(values[index].Field.Name, values[index].Value));
        }
        return new(properties.ToImmutable());
    }

    internal bool Accepts(PluginValue.Map map)
    {
        if (map.Properties.Length != Fields.Length)
        {
            return false;
        }
        var properties = map.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value,
            StringComparer.Ordinal
        );
        return Fields.All(field =>
            properties.TryGetValue(field.Name, out var value) && field.Shape.Accepts(value)
        );
    }
}
