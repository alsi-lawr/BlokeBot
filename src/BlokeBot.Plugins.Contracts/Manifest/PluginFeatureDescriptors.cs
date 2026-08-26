using System.Collections.Immutable;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

public enum PluginSettingScope
{
    Installation,
    Channel,
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(PluginSettingSchema.Boolean), "boolean")]
[TomlDerivedType(typeof(PluginSettingSchema.Text), "text")]
[TomlDerivedType(typeof(PluginSettingSchema.MultilineText), "multilineText")]
[TomlDerivedType(typeof(PluginSettingSchema.Integer), "integer")]
[TomlDerivedType(typeof(PluginSettingSchema.Number), "number")]
[TomlDerivedType(typeof(PluginSettingSchema.Duration), "duration")]
[TomlDerivedType(typeof(PluginSettingSchema.Choice), "choice")]
[TomlDerivedType(typeof(PluginSettingSchema.Secret), "secret")]
public abstract record PluginSettingSchema
{
    private PluginSettingSchema() { }

    public abstract TResult Match<TResult>(
        Func<Boolean, TResult> boolean,
        Func<Text, TResult> text,
        Func<MultilineText, TResult> multilineText,
        Func<Integer, TResult> integer,
        Func<Number, TResult> number,
        Func<Duration, TResult> duration,
        Func<Choice, TResult> choice,
        Func<Secret, TResult> secret
    );

    public sealed record Boolean : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => boolean(this);
    }

    public sealed record Text(int MaximumLength) : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => text(this);
    }

    public sealed record MultilineText(int MaximumLength) : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => multilineText(this);
    }

    public sealed record Integer(long Minimum, long Maximum) : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => integer(this);
    }

    public sealed record Number(decimal Minimum, decimal Maximum, int DecimalPlaces)
        : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => number(this);
    }

    public sealed record Duration(long MinimumSeconds, long MaximumSeconds) : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => duration(this);
    }

    public sealed record Choice(ImmutableArray<PluginSettingChoiceDescriptor> Choices)
        : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => choice(this);
    }

    public sealed record Secret(int MaximumLength) : PluginSettingSchema
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<MultilineText, TResult> multilineText,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice,
            Func<Secret, TResult> secret
        ) => secret(this);
    }
}

public sealed record PluginSettingChoiceDescriptor(PluginSettingChoiceId Id, string Name);

public sealed record PluginSettingDescriptor(
    PluginSettingId Id,
    string Name,
    string Description,
    PluginSettingScope Scope,
    bool Required,
    PluginSettingSchema Schema
);

public sealed record PluginFeatureDescriptor(
    PluginFeatureId Id,
    string Name,
    string Description,
    ImmutableArray<PluginSettingId> Settings,
    PluginTwitchRequirements Twitch,
    ImmutableArray<PluginAutomationTemplateId> AutomationTemplates,
    PluginDispatchDeclarations? Dispatch = null
)
{
    [TomlIgnore]
    public PluginDispatchDeclarations DispatchDeclarations =>
        Dispatch ?? PluginDispatchDeclarations.Empty;
}

public sealed record PluginTwitchRequirements(
    ImmutableArray<string> Scopes,
    ImmutableArray<string> EventSubTypes
);
