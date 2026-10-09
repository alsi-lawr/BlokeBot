using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Overlays.Full;

internal static class FullOverlayEventFeeds
{
    internal static bool TryConfiguration(
        FullOverlayWidget widget,
        out FullOverlayEventFeedConfiguration configuration
    )
    {
        configuration = null!;
        if (
            widget.Kind.Value != "event-feed"
            || widget.Configuration.ValueKind != JsonValueKind.Object
            || !widget.Configuration.TryGetProperty("bindingId", out var binding)
            || binding.ValueKind != JsonValueKind.String
            || !binding.TryGetGuid(out var id)
            || id == Guid.Empty
            || !widget.Configuration.TryGetProperty("feed", out var feed)
            || OverlayConfiguration.Parse(
                BlokeBot.Persistence.Models.OverlayType.EventFeed,
                feed.GetRawText()
            )
                is not OverlayConfigurationParseResult.Valid
                {
                    Value: OverlayConfiguration.EventFeedV1 value
                }
        )
        {
            return false;
        }
        configuration = new(id, value);
        return true;
    }

    // Presentation belongs to each widget; only queue/admission policy must agree on a shared binding.
    internal static string AdmissionConfiguration(OverlayConfiguration.EventFeedV1 feed) =>
        new OverlayConfiguration.EventFeedV1(
            feed.Capacity,
            feed.OverflowPolicy,
            feed.Kinds
        ).ToPersistenceJson();

    internal static ImmutableArray<FullOverlayEventFeedConfiguration> Bindings(
        FullOverlayDocument document
    ) =>
        [
            .. document
                .Widgets.Select(widget =>
                    TryConfiguration(widget, out var configuration) ? configuration : null
                )
                .OfType<FullOverlayEventFeedConfiguration>()
                .GroupBy(configuration => configuration.BindingId)
                .Where(group =>
                    group
                        .Select(configuration => AdmissionConfiguration(configuration.Feed))
                        .Distinct(StringComparer.Ordinal)
                        .Count() == 1
                )
                .Select(group => group.First()),
        ];

    internal static bool HasConflict(FullOverlayDocument document, Guid bindingId) =>
        document
            .Widgets.Select(widget =>
                TryConfiguration(widget, out var configuration) ? configuration : null
            )
            .OfType<FullOverlayEventFeedConfiguration>()
            .Where(configuration => configuration.BindingId == bindingId)
            .Select(configuration => AdmissionConfiguration(configuration.Feed))
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
}
