using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _overlayTypes =
        PersistedEnumTokens<OverlayType>.Values.ToArray();
    private static readonly string[] _overlayInstanceEventKinds =
        PersistedEnumTokens<OverlayInstanceEventKind>.Values.ToArray();
    private static readonly string[] _overlayCueQueuePolicies =
        PersistedEnumTokens<OverlayCueQueuePolicy>.Values.ToArray();
    private static readonly string[] _overlayEventFeedKinds =
        PersistedEnumTokens<OverlayEventFeedKind>.Values.ToArray();
    private static readonly string[] _overlayEventFeedPriorities =
        PersistedEnumTokens<OverlayEventFeedPriority>.Values.ToArray();
    private static readonly string[] _overlayEventFeedLifecycles =
        PersistedEnumTokens<OverlayEventFeedLifecycle>.Values.ToArray();
    private static readonly string[] _overlayMediaDocumentStates =
        PersistedEnumTokens<OverlayMediaDocumentState>.Values.ToArray();

    private void ConfigureOverlays(ModelBuilder modelBuilder)
    {
        ConfigureOverlayInstances(modelBuilder);
        ConfigureOverlayCues(modelBuilder);
        ConfigureOverlayMedia(modelBuilder);
        ConfigureOverlayEventFeed(modelBuilder);
    }
}
