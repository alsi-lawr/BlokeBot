using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Points.Commands;

public sealed record PointsCommandResolution(
    int HostId,
    PointsCommandKind Kind,
    PointsSettings Settings,
    ReplyDeliveryMap ReplyDelivery
);
