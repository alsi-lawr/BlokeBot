using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public abstract record PluginFrameReadOutcome
{
    private PluginFrameReadOutcome() { }

    public sealed record Message(PluginWorkerMessage Value) : PluginFrameReadOutcome;

    public sealed record EndOfStream : PluginFrameReadOutcome;

    public sealed record Rejected(PluginWorkerFailure Failure) : PluginFrameReadOutcome;
}

public abstract record PluginFrameWriteOutcome
{
    private PluginFrameWriteOutcome() { }

    public sealed record Written : PluginFrameWriteOutcome;

    public sealed record Rejected(PluginWorkerFailure Failure) : PluginFrameWriteOutcome;
}

public sealed class PluginWorkerProtocolCodec
{
    private static readonly JsonSerializerOptions _options = CreateOptions();
    private static readonly PluginWorkerJsonContext _json = new(_options);

    public async ValueTask<PluginFrameReadOutcome> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        var headerBytes = await ReadUpToAsync(stream, header, cancellationToken);
        if (headerBytes == 0)
        {
            return new PluginFrameReadOutcome.EndOfStream();
        }

        if (headerBytes != header.Length)
        {
            return Rejected(PluginWorkerFailureCode.MalformedFrame, "Truncated frame header.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > PluginWorkerLimits.MaximumFrameBytes)
        {
            return Rejected(
                length > PluginWorkerLimits.MaximumFrameBytes
                    ? PluginWorkerFailureCode.FrameTooLarge
                    : PluginWorkerFailureCode.MalformedFrame,
                "Invalid worker frame length."
            );
        }

        var payload = new byte[length];
        if (await ReadUpToAsync(stream, payload, cancellationToken) != length)
        {
            return Rejected(PluginWorkerFailureCode.MalformedFrame, "Truncated frame payload.");
        }

        try
        {
            var message = JsonSerializer.Deserialize(payload, _json.WorkerMessage);
            return message is null
                ? Rejected(PluginWorkerFailureCode.MalformedFrame, "Empty worker message.")
                : new PluginFrameReadOutcome.Message(message);
        }
        catch (JsonException)
        {
            return Rejected(PluginWorkerFailureCode.MalformedFrame, "Invalid worker message.");
        }
    }

    public async ValueTask<PluginFrameWriteOutcome> WriteAsync(
        Stream stream,
        PluginWorkerMessage message,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        if (
            PluginWorkerMessageValidator.Validate(message)
            is PluginWorkerMessageValidationOutcome.Rejected invalid
        )
        {
            return new PluginFrameWriteOutcome.Rejected(invalid.Failure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var payloadLength = MeasurePayload(message);
        if (payloadLength > PluginWorkerLimits.MaximumFrameBytes)
        {
            return new PluginFrameWriteOutcome.Rejected(
                new(
                    PluginWorkerFailureCode.FrameTooLarge,
                    "Worker message exceeds the frame limit."
                )
            );
        }

        var payload = new byte[payloadLength];
        using (var payloadStream = new MemoryStream(payload, writable: true))
        {
            payloadStream.SetLength(0);
            JsonSerializer.Serialize(payloadStream, message, _json.WorkerMessage);
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return new PluginFrameWriteOutcome.Written();
    }

    private static int MeasurePayload(PluginWorkerMessage message)
    {
        using var counter = new PluginWorkerMessageCountingStream();
        JsonSerializer.Serialize(counter, message, _json.WorkerMessage);
        return counter.BytesWritten > PluginWorkerLimits.MaximumFrameBytes
            ? PluginWorkerLimits.MaximumFrameBytes + 1
            : (int)counter.BytesWritten;
    }

    private static async ValueTask<int> ReadUpToAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken
    )
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = await stream.ReadAsync(destination[total..], cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static PluginFrameReadOutcome.Rejected Rejected(
        PluginWorkerFailureCode code,
        string message
    ) => new(new(code, message));

    private static JsonSerializerOptions CreateOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            AllowDuplicateProperties = false,
            AllowTrailingCommas = false,
            MaxDepth = PluginContractLimits.MaximumPluginValueDepth + 32,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) },
        };
}
