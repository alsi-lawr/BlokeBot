using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Functional;
using Microsoft.AspNetCore.DataProtection;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public interface IHostBotAccountTokenProtector
{
    Result<byte[], HostBotAccountTokenProtectionFailure> Protect(
        int hostId,
        HostBotAccountTokenPayload payload
    );

    Result<HostBotAccountTokenPayload, HostBotAccountTokenProtectionFailure> Unprotect(
        int hostId,
        byte[] protectedPayload
    );
}

public sealed record HostBotAccountTokenPayload(
    [property: JsonIgnore] string AccessToken,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset ExpiresAtUtc
)
{
    public override string ToString()
    {
        return $"{nameof(HostBotAccountTokenPayload)} {{ [REDACTED] }}";
    }
}

public sealed record HostBotAccountTokenProtectionFailure;

public sealed class DataProtectionHostBotAccountTokenProtector(
    IDataProtectionProvider protectionProvider
) : IHostBotAccountTokenProtector
{
    private const int _payloadVersion = 1;
    private const string _purpose = "BlokeBot.CustomBotTokenPayload";
    private const string _schemaPurpose = "schema-v1";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Result<byte[], HostBotAccountTokenProtectionFailure> Protect(
        int hostId,
        HostBotAccountTokenPayload payload
    )
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (
            hostId <= 0
            || string.IsNullOrWhiteSpace(payload.AccessToken)
            || string.IsNullOrWhiteSpace(payload.RefreshToken)
        )
        {
            return Failed<byte[]>();
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            new StoredPayload(
                _payloadVersion,
                payload.AccessToken,
                payload.RefreshToken,
                payload.ExpiresAtUtc
            ),
            _jsonOptions
        );
        try
        {
            return Result<byte[], HostBotAccountTokenProtectionFailure>.Success(
                Protector(hostId).Protect(plaintext)
            );
        }
        catch (CryptographicException)
        {
            return Failed<byte[]>();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Result<HostBotAccountTokenPayload, HostBotAccountTokenProtectionFailure> Unprotect(
        int hostId,
        byte[] protectedPayload
    )
    {
        ArgumentNullException.ThrowIfNull(protectedPayload);
        if (hostId <= 0 || protectedPayload.Length == 0)
        {
            return Failed<HostBotAccountTokenPayload>();
        }

        byte[] plaintext;
        try
        {
            plaintext = Protector(hostId).Unprotect(protectedPayload);
        }
        catch (CryptographicException)
        {
            return Failed<HostBotAccountTokenPayload>();
        }

        try
        {
            var payload = JsonSerializer.Deserialize<StoredPayload>(plaintext, _jsonOptions);
            return
                payload
                    is {
                        Version: _payloadVersion,
                        AccessToken.Length: > 0,
                        RefreshToken.Length: > 0,
                    }
                ? Result<HostBotAccountTokenPayload, HostBotAccountTokenProtectionFailure>.Success(
                    new HostBotAccountTokenPayload(
                        payload.AccessToken,
                        payload.RefreshToken,
                        payload.ExpiresAtUtc
                    )
                )
                : Failed<HostBotAccountTokenPayload>();
        }
        catch (JsonException)
        {
            return Failed<HostBotAccountTokenPayload>();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private IDataProtector Protector(int hostId)
    {
        return protectionProvider.CreateProtector(
            _purpose,
            _schemaPurpose,
            hostId.ToString(CultureInfo.InvariantCulture)
        );
    }

    private static Result<T, HostBotAccountTokenProtectionFailure> Failed<T>()
    {
        return Result<T, HostBotAccountTokenProtectionFailure>.Error(
            new HostBotAccountTokenProtectionFailure()
        );
    }

    private sealed record StoredPayload(
        int Version,
        string AccessToken,
        string RefreshToken,
        DateTimeOffset ExpiresAtUtc
    );
}
