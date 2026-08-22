using System.Buffers.Binary;

namespace BlokeBot.Plugins.Contracts;

internal static class PluginPackagePayloadPolicy
{
    private const int _dosHeaderPeOffset = 0x3C;
    private const int _coffHeaderSize = 20;
    private const int _peSignatureSize = 4;
    private const int _optionalHeaderMagicOffset = _peSignatureSize + _coffHeaderSize;
    private const int _optionalHeaderSizeOffset = _peSignatureSize + 16;
    private const int _pe32NumberOfDirectoriesOffset = 92;
    private const int _pe32PlusNumberOfDirectoriesOffset = 108;
    private const int _pe32ClrDirectoryOffset = 208;
    private const int _pe32PlusClrDirectoryOffset = 224;
    private const uint _minimumClrDirectoryCount = 15;

    internal static PluginPackageEntryErrorCode? Classify(ReadOnlySpan<byte> content) =>
        ClassifyPortableExecutable(content)
        ?? (
            StartsWith(content, 0x7F, 0x45, 0x4C, 0x46) || IsMachO(content)
                ? PluginPackageEntryErrorCode.NativePayloadNotPermitted
            : StartsWith(content, 0x00, 0x61, 0x73, 0x6D)
                ? PluginPackageEntryErrorCode.BrowserExecutablePayloadNotPermitted
            : null
        );

    private static PluginPackageEntryErrorCode? ClassifyPortableExecutable(
        ReadOnlySpan<byte> content
    )
    {
        if (!StartsWith(content, 0x4D, 0x5A) || content.Length < _dosHeaderPeOffset + sizeof(int))
        {
            return null;
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(
            content.Slice(_dosHeaderPeOffset, sizeof(int))
        );
        if (
            peOffset < 0
            || !HasBytes(content, peOffset, _optionalHeaderMagicOffset + sizeof(ushort))
            || !StartsWith(content[peOffset..], 0x50, 0x45, 0x00, 0x00)
        )
        {
            return null;
        }

        var optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(
            content.Slice(peOffset + _optionalHeaderSizeOffset, sizeof(ushort))
        );
        var optionalHeaderOffset = peOffset + _optionalHeaderMagicOffset;
        if (!HasBytes(content, optionalHeaderOffset, optionalHeaderSize))
        {
            return PluginPackageEntryErrorCode.NativePayloadNotPermitted;
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(
            content.Slice(optionalHeaderOffset, sizeof(ushort))
        );
        return HasClrDirectory(content, optionalHeaderOffset, optionalHeaderSize, magic)
            ? PluginPackageEntryErrorCode.DotNetPayloadNotPermitted
            : PluginPackageEntryErrorCode.NativePayloadNotPermitted;
    }

    private static bool HasClrDirectory(
        ReadOnlySpan<byte> content,
        int optionalHeaderOffset,
        int optionalHeaderSize,
        ushort magic
    )
    {
        var offsets = magic switch
        {
            0x010B => (_pe32NumberOfDirectoriesOffset, _pe32ClrDirectoryOffset),
            0x020B => (_pe32PlusNumberOfDirectoriesOffset, _pe32PlusClrDirectoryOffset),
            _ => (-1, -1),
        };
        if (
            offsets.Item1 < 0
            || optionalHeaderSize < offsets.Item2 + (2 * sizeof(uint))
            || !HasBytes(content, optionalHeaderOffset + offsets.Item2, 2 * sizeof(uint))
        )
        {
            return false;
        }

        var directoryCount = BinaryPrimitives.ReadUInt32LittleEndian(
            content.Slice(optionalHeaderOffset + offsets.Item1, sizeof(uint))
        );
        var clrDirectory = content.Slice(optionalHeaderOffset + offsets.Item2, 2 * sizeof(uint));
        return directoryCount >= _minimumClrDirectoryCount
            && BinaryPrimitives.ReadUInt32LittleEndian(clrDirectory) != 0
            && BinaryPrimitives.ReadUInt32LittleEndian(clrDirectory[sizeof(uint)..]) != 0;
    }

    private static bool IsMachO(ReadOnlySpan<byte> content) =>
        StartsWith(content, 0xFE, 0xED, 0xFA, 0xCE)
        || StartsWith(content, 0xFE, 0xED, 0xFA, 0xCF)
        || StartsWith(content, 0xCE, 0xFA, 0xED, 0xFE)
        || StartsWith(content, 0xCF, 0xFA, 0xED, 0xFE)
        || StartsWith(content, 0xCA, 0xFE, 0xBA, 0xBE)
        || StartsWith(content, 0xBE, 0xBA, 0xFE, 0xCA)
        || StartsWith(content, 0xCA, 0xFE, 0xBA, 0xBF)
        || StartsWith(content, 0xBF, 0xBA, 0xFE, 0xCA);

    private static bool StartsWith(ReadOnlySpan<byte> content, byte first, byte second) =>
        content.Length >= 2 && content[0] == first && content[1] == second;

    private static bool StartsWith(
        ReadOnlySpan<byte> content,
        byte first,
        byte second,
        byte third,
        byte fourth
    ) =>
        content.Length >= 4
        && content[0] == first
        && content[1] == second
        && content[2] == third
        && content[3] == fourth;

    private static bool HasBytes(ReadOnlySpan<byte> content, int offset, int length) =>
        offset >= 0 && length >= 0 && offset <= content.Length - length;
}
