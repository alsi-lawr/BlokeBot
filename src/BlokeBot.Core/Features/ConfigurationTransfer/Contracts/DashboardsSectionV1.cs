using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

public sealed record DashboardsSectionV1(
    [property: JsonRequired] IReadOnlyList<DashboardLayoutV1> Layouts
);

public sealed record DashboardLayoutV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] int Columns,
    [property: JsonRequired] IReadOnlyList<DashboardWidgetV1> Widgets
);

public sealed record DashboardWidgetV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Label,
    [property: JsonRequired] DashboardPlacementV1 Placement,
    [property: JsonRequired] DashboardConfigurationV1 Configuration
);

public sealed record DashboardPlacementV1(
    [property: JsonRequired] int Column,
    [property: JsonRequired] int Row,
    [property: JsonRequired] int Width,
    [property: JsonRequired] int Height,
    [property: JsonRequired] int Order
);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DashboardConfigurationV1.Automation), "automation")]
[JsonDerivedType(typeof(DashboardConfigurationV1.Value), "value")]
[JsonDerivedType(typeof(DashboardConfigurationV1.Countdown), "countdown")]
[JsonDerivedType(typeof(DashboardConfigurationV1.Poll), "poll")]
[JsonDerivedType(typeof(DashboardConfigurationV1.Prediction), "prediction")]
[JsonDerivedType(typeof(DashboardConfigurationV1.Giveaway), "giveaway")]
[JsonDerivedType(typeof(DashboardConfigurationV1.Guessing), "guessing")]
public abstract record DashboardConfigurationV1
{
    private DashboardConfigurationV1(DashboardReferenceV1 binding) => Binding = binding;

    [JsonRequired]
    public DashboardReferenceV1 Binding { get; init; }

    public sealed record Automation(DashboardReferenceV1 Binding, string? TextOverride = null)
        : DashboardConfigurationV1(Binding);

    public sealed record Value(DashboardReferenceV1 Binding, string? DictionaryKey = null)
        : DashboardConfigurationV1(Binding);

    public sealed record Countdown(
        DashboardReferenceV1 Binding,
        [property: JsonRequired] int DefaultSeconds
    ) : DashboardConfigurationV1(Binding);

    public sealed record Poll(DashboardReferenceV1 Binding) : DashboardConfigurationV1(Binding);

    public sealed record Prediction(DashboardReferenceV1 Binding)
        : DashboardConfigurationV1(Binding);

    public sealed record Giveaway(DashboardReferenceV1 Binding) : DashboardConfigurationV1(Binding);

    public sealed record Guessing(DashboardReferenceV1 Binding) : DashboardConfigurationV1(Binding);
}

public enum DashboardReferenceIssueV1
{
    NotSelected,
    Missing,
    Ambiguous,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DashboardReferenceV1.Automation), "automation")]
[JsonDerivedType(typeof(DashboardReferenceV1.Value), "value")]
[JsonDerivedType(typeof(DashboardReferenceV1.Countdown), "countdown")]
[JsonDerivedType(typeof(DashboardReferenceV1.Poll), "poll")]
[JsonDerivedType(typeof(DashboardReferenceV1.Prediction), "prediction")]
[JsonDerivedType(typeof(DashboardReferenceV1.Giveaway), "giveaway")]
[JsonDerivedType(typeof(DashboardReferenceV1.Guessing), "guessing")]
[JsonDerivedType(typeof(DashboardReferenceV1.Unconfigured), "unconfigured")]
public abstract record DashboardReferenceV1
{
    private DashboardReferenceV1() { }

    public sealed record Automation(
        [property: JsonRequired] string FlowName,
        string? SourceAlias,
        DashboardAutomationSourceV1? DocumentSource = null
    ) : DashboardReferenceV1;

    public sealed record Value(
        [property: JsonRequired] string Name,
        [property: JsonRequired] CustomValueScope Scope,
        [property: JsonRequired] CustomValueKind Kind
    ) : DashboardReferenceV1;

    public sealed record Countdown([property: JsonRequired] string Name) : DashboardReferenceV1;

    public sealed record Poll([property: JsonRequired] string Title) : DashboardReferenceV1;

    public sealed record Prediction([property: JsonRequired] string Title) : DashboardReferenceV1;

    public sealed record Giveaway : DashboardReferenceV1;

    public sealed record Guessing(
        [property: JsonRequired] string Slug,
        string? DocumentProfileId = null
    ) : DashboardReferenceV1;

    public sealed record Unconfigured(
        [property: JsonRequired] DashboardReferenceIssueV1 Issue,
        string? Reason = null
    ) : DashboardReferenceV1;
}

public sealed record DashboardAutomationSourceV1(
    [property: JsonRequired] string FlowId,
    [property: JsonRequired] string NodeId
);
