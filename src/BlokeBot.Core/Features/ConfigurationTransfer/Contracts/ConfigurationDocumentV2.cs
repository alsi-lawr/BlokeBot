using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationDocumentV2(
    [property: JsonRequired] string Format,
    [property: JsonRequired] int Version,
    [property: JsonRequired] DateTimeOffset ExportedAtUtc,
    [property: JsonRequired] ConfigurationSourceV1 Source,
    [property: JsonRequired] ConfigurationSectionsV2 Sections
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationSourceV1(
    [property: JsonRequired] string ChannelLogin,
    string? BlokeBotVersion
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationSectionsV2(
    CustomCommandsSectionV1? CustomCommands = null,
    AnnouncementsSectionV1? Announcements = null,
    GuessingSectionV1? Guessing = null,
    PointsSectionV1? Points = null,
    ChannelToolEnablementV1? ChannelToolEnablement = null,
    OverlaysSectionV1? Overlays = null,
    AutomationsSectionV2? Automations = null
);

internal sealed record ConfigurationDocumentHeader(
    [property: JsonRequired] string Format,
    [property: JsonRequired] int Version
);
