using System.Diagnostics;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Dashboards;

internal enum DashboardBindingIssue
{
    NotSelected,
    Missing,
    Ambiguous,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(DashboardBinding.Configured), "configured")]
[JsonDerivedType(typeof(DashboardBinding.Unconfigured), "unconfigured")]
internal abstract record DashboardBinding
{
    private DashboardBinding() { }

    internal TResult Match<TResult>(
        Func<Configured, TResult> configured,
        Func<Unconfigured, TResult> unconfigured
    ) =>
        this switch
        {
            Configured value => configured(value),
            Unconfigured value => unconfigured(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Configured(DashboardTarget Target) : DashboardBinding;

    internal sealed record Unconfigured(DashboardBindingIssue Issue, string? Reference = null)
        : DashboardBinding;
}

internal enum DashboardBindingAvailability
{
    Ready,
    Unconfigured,
    Missing,
    FeatureDisabled,
}

internal sealed record DashboardWidgetBindingState(
    DashboardWidgetId WidgetId,
    DashboardBindingAvailability Availability
);
