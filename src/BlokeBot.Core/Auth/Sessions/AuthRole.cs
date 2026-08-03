using BlokeBot.Functional;

namespace BlokeBot.Core.Auth.Sessions;

public enum AuthRole
{
    Admin,
    Bot,
    Moderator,
    Streamer,
}

internal static class AuthRoleCodec
{
    public static string Encode(AuthRole role) =>
        role switch
        {
            AuthRole.Admin => "admin",
            AuthRole.Bot => "bot",
            AuthRole.Moderator => "moderator",
            AuthRole.Streamer => "streamer",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    public static Result<AuthRole, AuthRoleDecodeFailure> Decode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Result<AuthRole, AuthRoleDecodeFailure>.Error(new AuthRoleDecodeFailure())
            : value.Trim().ToLowerInvariant() switch
            {
                "admin" => Result<AuthRole, AuthRoleDecodeFailure>.Success(AuthRole.Admin),
                "bot" => Result<AuthRole, AuthRoleDecodeFailure>.Success(AuthRole.Bot),
                "moderator" => Result<AuthRole, AuthRoleDecodeFailure>.Success(AuthRole.Moderator),
                "streamer" => Result<AuthRole, AuthRoleDecodeFailure>.Success(AuthRole.Streamer),
                _ => Result<AuthRole, AuthRoleDecodeFailure>.Error(new AuthRoleDecodeFailure()),
            };
}

internal readonly record struct AuthRoleDecodeFailure;
