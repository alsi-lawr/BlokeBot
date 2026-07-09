using BlokeBot.Features.Replies;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Commands;

public sealed record PointsCommandResolution(
    int HostId,
    PointsCommandKind Kind,
    PointsSettings Settings,
    ReplyDeliveryMap ReplyDelivery
);
