using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Plugins.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace BlokeBot.Plugins.Features;

public sealed class PluginSecretPlaintext
{
    private PluginSecretPlaintext(string value) => Value = value;

    internal string Value { get; }

    internal int Length => Value.Length;

    public static bool TryCreate(
        string? candidate,
        int maximumLength,
        out PluginSecretPlaintext secret
    )
    {
        var valid = candidate is { Length: > 0 } && candidate.Length <= maximumLength;
        secret = valid ? new(candidate!) : null!;
        return valid;
    }
}

public sealed record PluginProtectedSecret(ReadOnlyMemory<byte> Bytes);

public abstract record PluginSecretUpdate
{
    private PluginSecretUpdate() { }

    public abstract TResult Match<TResult>(
        Func<Keep, TResult> keep,
        Func<Replace, TResult> replace,
        Func<Clear, TResult> clear
    );

    public sealed record Keep : PluginSecretUpdate
    {
        public override TResult Match<TResult>(
            Func<Keep, TResult> keep,
            Func<Replace, TResult> replace,
            Func<Clear, TResult> clear
        ) => keep(this);
    }

    public sealed record Replace(PluginSecretPlaintext Value) : PluginSecretUpdate
    {
        public override TResult Match<TResult>(
            Func<Keep, TResult> keep,
            Func<Replace, TResult> replace,
            Func<Clear, TResult> clear
        ) => replace(this);
    }

    public sealed record Clear : PluginSecretUpdate
    {
        public override TResult Match<TResult>(
            Func<Keep, TResult> keep,
            Func<Replace, TResult> replace,
            Func<Clear, TResult> clear
        ) => clear(this);
    }
}

public sealed record PluginSecretUpdateEntry(PluginSettingId SettingId, PluginSecretUpdate Update);

public sealed record PluginProtectedSecretEntry(
    PluginSettingId SettingId,
    PluginProtectedSecret Value
);

public sealed record PluginSecretPresence(PluginSettingId SettingId, bool HasValue);

public abstract record PluginSecretKey
{
    private PluginSecretKey() { }

    public abstract TResult Match<TResult>(
        Func<Installation, TResult> installation,
        Func<Feature, TResult> feature
    );

    public sealed record Installation(PluginId PluginId, PluginSettingId SettingId)
        : PluginSecretKey
    {
        public override TResult Match<TResult>(
            Func<Installation, TResult> installation,
            Func<Feature, TResult> feature
        ) => installation(this);
    }

    public sealed record Feature(PluginFeatureKey Key, PluginSettingId SettingId) : PluginSecretKey
    {
        public override TResult Match<TResult>(
            Func<Installation, TResult> installation,
            Func<Feature, TResult> feature
        ) => feature(this);
    }
}

public sealed record PluginSecretChanges(
    ImmutableArray<PluginProtectedSecretEntry> Replacements,
    ImmutableArray<PluginSettingId> Clears
)
{
    public static PluginSecretChanges Empty { get; } = new([], []);
}

public interface IPluginSecretProtector
{
    PluginProtectedSecret Protect(PluginSecretKey key, PluginSecretPlaintext plaintext);

    PluginSecretUnprotectOutcome Unprotect(
        PluginSecretKey key,
        PluginProtectedSecret protectedSecret
    );
}

public abstract record PluginSecretUnprotectOutcome
{
    private PluginSecretUnprotectOutcome() { }

    public sealed record Unprotected(PluginSecretPlaintext Value) : PluginSecretUnprotectOutcome;

    public sealed record Failed : PluginSecretUnprotectOutcome;
}

public sealed class DataProtectionPluginSecretProtector(IDataProtectionProvider dataProtection)
    : IPluginSecretProtector
{
    private static readonly UTF8Encoding _utf8 = new(false, true);

    public PluginProtectedSecret Protect(PluginSecretKey key, PluginSecretPlaintext plaintext)
    {
        var protector = dataProtection.CreateProtector(Purposes(key));
        var bytes = Encoding.UTF8.GetBytes(plaintext.Value);
        try
        {
            return new(protector.Protect(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public PluginSecretUnprotectOutcome Unprotect(
        PluginSecretKey key,
        PluginProtectedSecret protectedSecret
    )
    {
        byte[]? plaintext = null;
        try
        {
            plaintext = dataProtection
                .CreateProtector(Purposes(key))
                .Unprotect(protectedSecret.Bytes.ToArray());
            var value = _utf8.GetString(plaintext);
            return PluginSecretPlaintext.TryCreate(
                value,
                PluginContractLimits.MaximumSecretSettingCharacters,
                out var secret
            )
                ? new PluginSecretUnprotectOutcome.Unprotected(secret)
                : new PluginSecretUnprotectOutcome.Failed();
        }
        catch (Exception exception)
            when (exception is CryptographicException or DecoderFallbackException)
        {
            return new PluginSecretUnprotectOutcome.Failed();
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static string[] Purposes(PluginSecretKey key) =>
        key.Match<string[]>(
            installation =>
                [
                    "BlokeBot.Plugins.Features.Secret.v1",
                    "installation",
                    installation.PluginId.Value,
                    installation.SettingId.Value,
                ],
            feature =>
                [
                    "BlokeBot.Plugins.Features.Secret.v1",
                    "feature",
                    feature.Key.PluginId.Value,
                    feature.Key.FeatureId.Value,
                    feature.Key.HostId.Value.ToString(CultureInfo.InvariantCulture),
                    feature.SettingId.Value,
                ]
        );
}
