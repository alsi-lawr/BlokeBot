using System.Security.Claims;
using BlokeBot.Functional;
using BlokeBot.Hosts;

namespace BlokeBot.Auth.Sessions;

public sealed record AuthenticatedSession
{
    public static AuthenticatedSession Anonymous { get; } = new();

    public bool IsAuthenticated { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Login { get; init; } = string.Empty;
    public string? ProfileImageUrl { get; init; }
    public bool CanCreateHost { get; init; }
    public bool IsBotAdmin { get; init; }
    public bool IsBotAccount { get; init; }
    public AuthRole? Role { get; init; }
    public IReadOnlyList<BotHostChoice> AvailableHosts { get; init; } = [];
    public AuthSessionState State { get; init; } = new AuthSessionState.NoSelection();
    public string? AdminEditingLogin { get; init; }
    public BotHostChoice? AdminReturnHost { get; init; }

    public bool IsAdminEditing => !string.IsNullOrWhiteSpace(AdminEditingLogin);

    public string DisplayRole =>
        IsBotAccount ? AuthRoleCodec.Encode(AuthRole.Bot)
        : State.Match<AuthRole?>(_ => null, selected => selected.Selection.Current.Role, _ => null)
            is { } hostRole
            ? AuthRoleCodec.Encode(hostRole)
        : Role is { } role ? AuthRoleCodec.Encode(role)
        : "operator";

    public string DisplayText =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(Login) ? Login
        : "Twitch user";

    public bool HasCapability(AuthSessionCapability capability)
    {
        return capability switch
        {
            AuthSessionCapability.BotAdmin => IsBotAdmin,
            AuthSessionCapability.HostSelected => State.Match(_ => false, _ => true, _ => false),
            AuthSessionCapability.Operator => !IsBotAccount
                && State.Match(
                    _ => false,
                    selected =>
                        selected.Selection.Current.Role
                            is AuthRole.Admin
                                or AuthRole.Streamer
                                or AuthRole.Moderator,
                    _ => false
                ),
            _ => false,
        };
    }

    public bool CanOpenHostConfig(IReadOnlySet<int> existingHostIds)
    {
        if (IsBotAccount)
        {
            return false;
        }

        return CanCreateHost
            || (
                State.Match(
                    _ => false,
                    selected =>
                        existingHostIds.Contains(selected.Selection.Current.Id)
                        && selected.Selection.Current.Role == AuthRole.Streamer,
                    _ => false
                )
            );
    }

    public bool CanUseBotFunctions(IReadOnlySet<int> existingHostIds)
    {
        return !IsBotAccount
            && State.Match(
                _ => false,
                selected => existingHostIds.Contains(selected.Selection.Current.Id),
                _ => false
            );
    }

    public bool CanAuthorizeSelectedHost =>
        State.Match(
            _ => false,
            selected =>
                selected.Selection.Current.Role == AuthRole.Streamer
                && string.Equals(
                    selected.Selection.Current.Login,
                    Login,
                    StringComparison.OrdinalIgnoreCase
                ),
            _ => false
        );

    public bool CurrentHostRoleIs(AuthRole role)
    {
        return State.Match(
            _ => false,
            selected => selected.Selection.Current.Role == role,
            _ => false
        );
    }

    public static AuthenticatedSession FromPrincipal(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Anonymous;
        }

        var hostClaims = DecodeHostClaims(user);
        var roleClaim = DecodeOptionalRole(user.FindFirstValue(AuthClaims.Role));
        var adminReturnHostClaim = DecodeOptionalHost(
            user.FindFirstValue(BotHostClaims.AdminReturnHost)
        );
        var claims = hostClaims.Match(
            valid =>
                roleClaim.Bind(role =>
                    adminReturnHostClaim.Map(adminReturnHost => new DecodedSessionClaims(
                        valid.Hosts,
                        role,
                        adminReturnHost
                    ))
                ),
            _ => Result<DecodedSessionClaims, InvalidSessionClaims>.Error(new())
        );
        var decoded = claims.Match(
            valid => new DecodedSession(
                valid.AvailableHosts,
                valid.Role,
                valid.AdminReturnHost,
                ParseHostSelection(user, valid.AvailableHosts)
            ),
            _ => new DecodedSession(
                hostClaims.Hosts,
                roleClaim.Match(value => value, _ => null),
                adminReturnHostClaim.Match(value => value, _ => null),
                new AuthSessionState.Invalid()
            )
        );

        return new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            DisplayName = user.Identity?.Name ?? string.Empty,
            Login = user.FindFirstValue(AuthClaims.Login) ?? string.Empty,
            ProfileImageUrl = user.FindFirstValue(AuthClaims.ProfileImageUrl),
            CanCreateHost = BooleanClaim(user, AuthClaims.CanCreateHost),
            IsBotAdmin = BooleanClaim(user, AuthClaims.IsBotAdmin),
            IsBotAccount = BooleanClaim(user, AuthClaims.IsBotAccount),
            Role = decoded.Role,
            AvailableHosts = decoded.AvailableHosts,
            State = decoded.State,
            AdminEditingLogin = user.FindFirstValue(BotHostClaims.AdminEditingLogin),
            AdminReturnHost = decoded.AdminReturnHost,
        };
    }

    private static AuthSessionState ParseHostSelection(
        ClaimsPrincipal user,
        IReadOnlyList<BotHostChoice> availableHosts
    )
    {
        var selectedValue = user.FindFirstValue(BotHostClaims.SelectedHost);
        if (string.IsNullOrWhiteSpace(selectedValue))
        {
            return new AuthSessionState.NoSelection();
        }

        if (availableHosts.Count == 0)
        {
            return new AuthSessionState.Invalid();
        }

        var selectedHost = BotHostClaimCodec.Decode(selectedValue);
        if (selectedHost is null)
        {
            return new AuthSessionState.Invalid();
        }

        var current = availableHosts.FirstOrDefault(host =>
            BotHostClaimCodec.Equivalent(host, selectedHost)
        );
        if (current is null)
        {
            return new AuthSessionState.Invalid();
        }

        return new AuthSessionState.Selected(new BotHostSelection(current, availableHosts));
    }

    private static DecodedHostClaims DecodeHostClaims(ClaimsPrincipal user)
    {
        var hosts = new List<BotHostChoice>();
        DecodedHostClaims result = new DecodedHostClaims.Valid([]);
        foreach (var claim in user.FindAll(BotHostClaims.AvailableHost))
        {
            if (BotHostClaimCodec.Decode(claim.Value) is { } host)
            {
                hosts.Add(host);
                continue;
            }

            result = new DecodedHostClaims.Invalid([]);
        }

        var ordered = hosts.OrderBy(host => host.DisplayName).ToArray();
        return result.Match<DecodedHostClaims>(
            _ => new DecodedHostClaims.Valid(ordered),
            _ => new DecodedHostClaims.Invalid(ordered)
        );
    }

    private static Result<AuthRole?, InvalidSessionClaims> DecodeOptionalRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<AuthRole?, InvalidSessionClaims>.Success(null);
        }

        var role = AuthRoleCodec.Decode(value).Match<AuthRole?>(decoded => decoded, _ => null);
        if (role is not null)
        {
            return Result<AuthRole?, InvalidSessionClaims>.Success(role);
        }

        return Result<AuthRole?, InvalidSessionClaims>.Error(new());
    }

    private static Result<BotHostChoice?, InvalidSessionClaims> DecodeOptionalHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<BotHostChoice?, InvalidSessionClaims>.Success(null);
        }

        if (BotHostClaimCodec.Decode(value) is { } host)
        {
            return Result<BotHostChoice?, InvalidSessionClaims>.Success(host);
        }

        return Result<BotHostChoice?, InvalidSessionClaims>.Error(new());
    }

    private static bool BooleanClaim(ClaimsPrincipal user, string claimType)
    {
        return string.Equals(
            user.FindFirstValue(claimType),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private sealed record DecodedSessionClaims(
        BotHostChoice[] AvailableHosts,
        AuthRole? Role,
        BotHostChoice? AdminReturnHost
    );

    private sealed record DecodedSession(
        BotHostChoice[] AvailableHosts,
        AuthRole? Role,
        BotHostChoice? AdminReturnHost,
        AuthSessionState State
    );

    private readonly record struct InvalidSessionClaims;

    private abstract record DecodedHostClaims(BotHostChoice[] Hosts)
    {
        internal abstract TResult Match<TResult>(
            Func<Valid, TResult> valid,
            Func<Invalid, TResult> invalid
        );

        internal sealed record Valid(BotHostChoice[] Hosts) : DecodedHostClaims(Hosts)
        {
            internal override TResult Match<TResult>(
                Func<Valid, TResult> valid,
                Func<Invalid, TResult> invalid
            )
            {
                return valid(this);
            }
        }

        internal sealed record Invalid(BotHostChoice[] Hosts) : DecodedHostClaims(Hosts)
        {
            internal override TResult Match<TResult>(
                Func<Valid, TResult> valid,
                Func<Invalid, TResult> invalid
            )
            {
                return invalid(this);
            }
        }
    }
}
