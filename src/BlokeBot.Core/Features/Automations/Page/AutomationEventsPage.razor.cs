using System.Collections.Immutable;
using BlokeBot.Core.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEventsPage
{
    private TwitchEventSourceReadinessOutcome? _outcome;
    private bool _loading = true;
    private bool _loadFailed;

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            _ = await LoadPageContextAsync();
            _outcome =
                HostId == 0
                    ? null
                    : await _readiness.LoadAsync(new(HostId), CancellationToken.None);
        }
        catch (Exception exception)
        {
            _outcome = null;
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            _loading = false;
        }
    }

    private static bool RequiresReconnect(TwitchEventSourceReadinessOutcome.Available available) =>
        available.Sources.Any(static source =>
            source.State is not TwitchEventSourceReadinessState.Ready
        );

    private static string ReconnectDescription(
        TwitchEventSourceReadinessOutcome.Available available
    )
    {
        if (!available.BroadcasterConnected)
        {
            return "Some event sources need this channel's Twitch integration. Reconnect so BlokeBot can subscribe to subscription, cheer, and Hype Train events.";
        }

        var missing = available
            .Sources.Select(static source => source.State)
            .OfType<TwitchEventSourceReadinessState.MissingScopes>()
            .SelectMany(static state => state.Scopes)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return $"Your saved Twitch connection is missing {FormatScopes(missing)}. Reconnect to approve the updated permissions.";
    }

    private static string StateToken(TwitchEventSourceReadinessState state) =>
        state switch
        {
            TwitchEventSourceReadinessState.Ready => "ready",
            TwitchEventSourceReadinessState.MissingScopes => "missing-scopes",
            _ => "not-connected",
        };

    private static string StateLabel(TwitchEventSourceReadinessState state) =>
        state switch
        {
            TwitchEventSourceReadinessState.Ready => "Ready",
            TwitchEventSourceReadinessState.MissingScopes => "Reconnect needed",
            _ => "Twitch connection needed",
        };

    private static string BadgeClass(TwitchEventSourceReadinessState state) =>
        state is TwitchEventSourceReadinessState.Ready
            ? "inline-flex shrink-0 items-center rounded-full bg-emerald-100 px-3 py-1 text-xs font-bold text-emerald-800"
            : "inline-flex shrink-0 items-center rounded-full bg-amber-100 px-3 py-1 text-xs font-bold text-amber-800";

    private static string ScopesLabel(TwitchEventSourceReadiness source) =>
        source.RequiredBroadcasterScopes.IsEmpty
            ? "None beyond the bot connection."
            : FormatScopes(source.RequiredBroadcasterScopes);

    private static string FormatScopes(ImmutableArray<string> scopes) => string.Join(", ", scopes);
}
