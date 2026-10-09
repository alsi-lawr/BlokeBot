using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Overlays.Full;

internal enum FullOverlayDataMode
{
    Live,
    Sample,
}

// Construct from a resolved publication, or an authorized private preview. Never send this context
// to authored/browser code. Preview has no Version; its CuePlans are non-consuming owner projections.
internal sealed record FullOverlayRenderContext(
    int HostId,
    Guid OverlayId,
    FullOverlayVersion? Version,
    FullOverlayRevision? SelectionRevision,
    FullOverlayDocument Document,
    FullOverlayDataMode DataMode,
    ImmutableArray<OverlayCuePlaybackPlan> CuePlans
);

internal sealed record FullOverlayWidgetDescriptor(
    FullOverlayWidgetKind Kind,
    string Name,
    Type ConfigurationType,
    ImmutableArray<PluginWidgetConfigurationField> PluginConfigurationFields
);

internal abstract record FullOverlayWidgetOutput
{
    private FullOverlayWidgetOutput() { }

    public abstract TResult Match<TResult>(
        Func<Source, TResult> source,
        Func<CuePlayer, TResult> cuePlayer,
        Func<Html, TResult> html,
        Func<Web, TResult> web,
        Func<Media, TResult> media,
        Func<Plugin, TResult> plugin,
        Func<Unavailable, TResult> unavailable
    );

    internal sealed record Source(OverlaySnapshotProjection Projection) : FullOverlayWidgetOutput
    {
        public override TResult Match<TResult>(
            Func<Source, TResult> source,
            Func<CuePlayer, TResult> cuePlayer,
            Func<Html, TResult> html,
            Func<Web, TResult> web,
            Func<Media, TResult> media,
            Func<Plugin, TResult> plugin,
            Func<Unavailable, TResult> unavailable
        ) => source(this);
    }

    internal sealed record CuePlayer(ImmutableArray<OverlayCuePlaybackPlan> Plans)
        : FullOverlayWidgetOutput
    {
        public override TResult Match<TResult>(
            Func<Source, TResult> source,
            Func<CuePlayer, TResult> cuePlayer,
            Func<Html, TResult> html,
            Func<Web, TResult> web,
            Func<Media, TResult> media,
            Func<Plugin, TResult> plugin,
            Func<Unavailable, TResult> unavailable
        ) => cuePlayer(this);
    }

    internal sealed record Html(string Content, string Css) : FullOverlayWidgetOutput
    {
        public override TResult Match<TResult>(
            Func<Source, TResult> source,
            Func<CuePlayer, TResult> cuePlayer,
            Func<Html, TResult> html,
            Func<Web, TResult> web,
            Func<Media, TResult> media,
            Func<Plugin, TResult> plugin,
            Func<Unavailable, TResult> unavailable
        ) => html(this);
    }

    internal sealed record Web(Uri Url) : FullOverlayWidgetOutput
    {
        public override TResult Match<TResult>(
            Func<Source, TResult> source,
            Func<CuePlayer, TResult> cuePlayer,
            Func<Html, TResult> html,
            Func<Web, TResult> web,
            Func<Media, TResult> media,
            Func<Plugin, TResult> plugin,
            Func<Unavailable, TResult> unavailable
        ) => web(this);
    }

    internal sealed record Media(Guid AssetId, int ContentRevision, string ContentType, bool Loop)
        : FullOverlayWidgetOutput
    {
        public override TResult Match<TResult>(
            Func<Source, TResult> source,
            Func<CuePlayer, TResult> cuePlayer,
            Func<Html, TResult> html,
            Func<Web, TResult> web,
            Func<Media, TResult> media,
            Func<Plugin, TResult> plugin,
            Func<Unavailable, TResult> unavailable
        ) => media(this);
    }

    internal sealed record Plugin(
        [property: JsonIgnore] PluginWidgetEndpoint Endpoint,
        PluginValue.Map State
    ) : FullOverlayWidgetOutput
    {
        public override TResult Match<TResult>(
            Func<Source, TResult> source,
            Func<CuePlayer, TResult> cuePlayer,
            Func<Html, TResult> html,
            Func<Web, TResult> web,
            Func<Media, TResult> media,
            Func<Plugin, TResult> plugin,
            Func<Unavailable, TResult> unavailable
        ) => plugin(this);
    }

    internal sealed record Unavailable(FullOverlayDiagnostic Diagnostic) : FullOverlayWidgetOutput
    {
        public override TResult Match<TResult>(
            Func<Source, TResult> source,
            Func<CuePlayer, TResult> cuePlayer,
            Func<Html, TResult> html,
            Func<Web, TResult> web,
            Func<Media, TResult> media,
            Func<Plugin, TResult> plugin,
            Func<Unavailable, TResult> unavailable
        ) => unavailable(this);
    }
}

internal sealed record FullOverlayWidgetProjection(
    FullOverlayWidgetId Id,
    FullOverlayAudio Audio,
    FullOverlayWidgetOutput Output
);

internal sealed record FullOverlayHtmlConfiguration(string Html, string Css);

internal sealed record FullOverlayWebConfiguration(string Url);

internal sealed record FullOverlayMediaConfiguration(Guid AssetId, bool Loop);

internal sealed record FullOverlayEventFeedConfiguration(
    Guid BindingId,
    OverlayConfiguration.EventFeedV1 Feed
);
