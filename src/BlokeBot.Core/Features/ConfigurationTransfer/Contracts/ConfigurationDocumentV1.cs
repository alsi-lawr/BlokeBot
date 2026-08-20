using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationDocumentV1(
    string Format,
    int Version,
    DateTimeOffset ExportedAtUtc,
    ConfigurationSourceV1 Source,
    ConfigurationSectionsV1 Sections
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationSourceV1(string ChannelLogin, string? BlokeBotVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationSectionsV1(
    CustomCommandsSectionV1? CustomCommands = null,
    AnnouncementsSectionV1? Announcements = null,
    GuessingSectionV1? Guessing = null,
    PointsSectionV1? Points = null,
    ChannelToolEnablementV1? ChannelToolEnablement = null
);

internal sealed record ConfigurationDocumentHeader(string Format, int Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ConfigurationDocumentV0(
    string Format,
    int Version,
    DateTimeOffset ExportedAtUtc,
    string ChannelLogin,
    ConfigurationSectionsV1 Sections
);
