using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationRandomNumberConfiguration(long Minimum = 0, long Maximum = 100)
    : AutomationConfiguration;

internal interface IAutomationIntegerEntropy
{
    long NextInt64Inclusive(long minimum, long maximum);
}

internal interface IAutomationUInt64Source
{
    ulong NextUInt64();
}

internal sealed class AutomationProductionIntegerEntropy(IAutomationUInt64Source? source = null)
    : IAutomationIntegerEntropy
{
    private readonly IAutomationUInt64Source _source = source ?? new CryptographicUInt64Source();

    public long NextInt64Inclusive(long minimum, long maximum) =>
        AutomationInclusiveIntegerMapping.NextInt64Inclusive(_source, minimum, maximum);

    private sealed class CryptographicUInt64Source : IAutomationUInt64Source
    {
        public ulong NextUInt64()
        {
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            RandomNumberGenerator.Fill(bytes);
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }
    }
}

internal sealed class AutomationSeededIntegerEntropy(ulong seed) : IAutomationIntegerEntropy
{
    private readonly SeededUInt64Source _source = new(seed);

    public long NextInt64Inclusive(long minimum, long maximum) =>
        AutomationInclusiveIntegerMapping.NextInt64Inclusive(_source, minimum, maximum);

    private sealed class SeededUInt64Source(ulong state) : IAutomationUInt64Source
    {
        private ulong _state = state;

        public ulong NextUInt64()
        {
            _state += 0x9e3779b97f4a7c15UL;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }
    }
}

internal static class AutomationInclusiveIntegerMapping
{
    internal static long NextInt64Inclusive(
        IAutomationUInt64Source source,
        long minimum,
        long maximum
    )
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }

        var lower = ToOrderedUnsigned(minimum);
        var upper = ToOrderedUnsigned(maximum);
        var width = unchecked(upper - lower + 1);
        if (width == 0)
        {
            return FromOrderedUnsigned(source.NextUInt64());
        }

        var threshold = unchecked(0UL - width) % width;
        while (true)
        {
            var product = (UInt128)source.NextUInt64() * width;
            if ((ulong)product < threshold)
            {
                continue;
            }

            var offset = (ulong)(product >> 64);
            return FromOrderedUnsigned(lower + offset);
        }
    }

    private static ulong ToOrderedUnsigned(long value) => unchecked((ulong)(value ^ long.MinValue));

    private static long FromOrderedUnsigned(ulong value) =>
        unchecked((long)(value ^ 0x8000000000000000UL));
}

internal sealed class AutomationRandomNumberHandler : IAutomationPureNodeHandler
{
    private static readonly AutomationPortId _numberOutput = new("number");

    public AutomationPureHandlerContract Contract { get; } =
        new(
            AutomationDefinitionIds.RandomNumber,
            AutomationNodeKind.Value,
            [],
            [
                new(
                    _numberOutput,
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable
                ),
            ]
        );

    public ValueTask<AutomationPureNodeResult> ExecuteAsync(
        AutomationPureNodeInput input,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Configuration is not AutomationRandomNumberConfiguration configuration)
        {
            return ValueTask.FromResult<AutomationPureNodeResult>(
                new AutomationPureNodeResult.Failed("configuration-invalid")
            );
        }

        var value = input.IntegerEntropy.NextInt64Inclusive(
            configuration.Minimum,
            configuration.Maximum
        );
        return ValueTask.FromResult<AutomationPureNodeResult>(
            new AutomationPureNodeResult.Succeeded(
                ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                    _numberOutput,
                    new(
                        new AutomationValue.Number(value),
                        [AutomationValueProvenance.Generated],
                        ValueFreeDiagnostic: true
                    )
                )
            )
        );
    }
}
