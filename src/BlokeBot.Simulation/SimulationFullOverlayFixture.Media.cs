using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Simulation;

internal static partial class SimulationFullOverlayFixture
{
    private static async Task<Guid?> UploadAudioAsync(
        OverlayCueService media,
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + 16000);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(8000);
            writer.Write(16000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(16000);
            writer.Write(new byte[16000]);
        }
        stream.Position = 0;
        return
            (await media.UploadAssetAsync(session, "Simulation audio", "audio/wav", stream, ct))
                is OverlayCueResult<OverlayMediaAssetView>.Succeeded uploaded
            ? uploaded.Value.Id
            : null;
    }

    private static async Task<Guid?> CreateAudioCueAsync(
        OverlayCueService media,
        AuthenticatedSession session,
        Guid assetId,
        CancellationToken ct
    )
    {
        var configuration = OverlayCueConfiguration.Create([
            new OverlayCueLayer.UploadedMedia
            {
                AssetId = assetId,
                MediaKind = OverlayCueMediaKind.Audio,
                Volume = .8m,
                Fit = OverlayCueFitMode.Contain,
                Rectangle = new(0, 0, 100, 100),
                StartOffsetMilliseconds = 0,
                DurationMilliseconds = 8000,
                ZIndex = 0,
            },
        ]);
        return
            configuration is OverlayCueConfigurationResult.Valid valid
            && await media.SaveCueAsync(
                session,
                new(
                    null,
                    new(0),
                    "Full delivery browser audio",
                    true,
                    8000,
                    OverlayCueQueuePolicy.Enqueue,
                    valid.Value.ToPersistenceJson()
                ),
                ct
            )
                is OverlayCueResult<OverlayCueView>.Succeeded saved
            ? saved.Value.Id
            : null;
    }
}
