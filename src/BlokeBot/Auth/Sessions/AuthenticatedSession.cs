using System.Security.Claims;
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
    public BotHostSelection? HostSelection { get; init; }
    public AuthSessionHostSelectionState HostSelectionState { get; init; }
    public bool ClaimsValid { get; init; } = true;
    public string? AdminEditingLogin { get; init; }
    public BotHostChoice? AdminReturnHost { get; init; }

    public bool IsAdminEditing => !string.IsNullOrWhiteSpace(AdminEditingLogin);

    public string DisplayRole =>
        IsBotAccount ? AuthRoleCodec.Encode(AuthRole.Bot)
        : HostSelection?.Current.Role is { } hostRole ? AuthRoleCodec.Encode(hostRole)
        : Role is { } role ? AuthRoleCodec.Encode(role)
        : "operator";

    public string DisplayText =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(Login) ? Login
        : "Twitch user";

    public bool HasCapability(AuthSessionCapability capability) =>
        capability switch
        {
            AuthSessionCapability.BotAdmin => IsBotAdmin,
            AuthSessionCapability.HostSelected => HostSelection is not null,
            AuthSessionCapability.Operator => HostSelection is not null
                && !IsBotAccount
                && (
                    CurrentHostRoleIs(AuthRole.Admin)
                    || CurrentHostRoleIs(AuthRole.Streamer)
                    || CurrentHostRoleIs(AuthRole.Moderator)
                ),
            _ => false,
        };

    public bool CanOpenHostConfig(IReadOnlySet<int> existingHostIds)
    {
        if (IsBotAccount)
            return false;

        return CanCreateHost
            || (
                HostSelection is not null
                && existingHostIds.Contains(HostSelection.Current.Id)
                && CurrentHostRoleIs(AuthRole.Streamer)
            );
    }

    public bool CanUseBotFunctions(IReadOnlySet<int> existingHostIds) =>
        !IsBotAccount
        && HostSelection is not null
        && existingHostIds.Contains(HostSelection.Current.Id);

    public bool CanAuthorizeSelectedHost =>
        HostSelection is not null
        && CurrentHostRoleIs(AuthRole.Streamer)
        && string.Equals(HostSelection.Current.Login, Login, StringComparison.OrdinalIgnoreCase);

    public bool CurrentHostRoleIs(AuthRole role) =>
        HostSelection is not null && HostSelection.Current.Role == role;

    public static AuthenticatedSession FromPrincipal(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return Anonymous;

        var claimsValid = true;
        var availableHosts = DecodeHostClaims(user, ref claimsValid);
        var role = DecodeOptionalRole(user.FindFirstValue(AuthClaims.Role), ref claimsValid);

        var hostSelection = ParseHostSelection(user, availableHosts, out var hostSelectionState);
        var adminReturnHost = DecodeOptionalHost(
            user.FindFirstValue(BotHostClaims.AdminReturnHost),
            ref claimsValid
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
            Role = role,
            AvailableHosts = availableHosts,
            HostSelection = hostSelection,
            HostSelectionState = hostSelectionState,
            ClaimsValid = claimsValid,
            AdminEditingLogin = user.FindFirstValue(BotHostClaims.AdminEditingLogin),
            AdminReturnHost = adminReturnHost,
        };
    }

    private static BotHostSelection? ParseHostSelection(
        ClaimsPrincipal user,
        IReadOnlyList<BotHostChoice> availableHosts,
        out AuthSessionHostSelectionState state
    )
    {
        var selectedValue = user.FindFirstValue(BotHostClaims.SelectedHost);
        if (string.IsNullOrWhiteSpace(selectedValue))
        {
            state = AuthSessionHostSelectionState.None;
            return null;
        }

        if (availableHosts.Count == 0)
        {
            state = AuthSessionHostSelectionState.Invalid;
            return null;
        }

        var selectedHost = BotHostClaimCodec.Decode(selectedValue);
        if (selectedHost is null)
        {
            state = AuthSessionHostSelectionState.Invalid;
            return null;
        }

        var current = availableHosts.FirstOrDefault(host =>
            BotHostClaimCodec.Equivalent(host, selectedHost)
        );
        if (current is null)
        {
            state = AuthSessionHostSelectionState.Invalid;
            return null;
        }

        state = AuthSessionHostSelectionState.Selected;
        return new BotHostSelection(current, availableHosts);
    }

    private static BotHostChoice[] DecodeHostClaims(ClaimsPrincipal user, ref bool claimsValid)
    {
        var hosts = new List<BotHostChoice>();
        foreach (var claim in user.FindAll(BotHostClaims.AvailableHost))
        {
            if (BotHostClaimCodec.Decode(claim.Value) is { } host)
            {
                hosts.Add(host);
                continue;
            }

            claimsValid = false;
        }

        return hosts.OrderBy(host => host.DisplayName).ToArray();
    }

    private static AuthRole? DecodeOptionalRole(string? value, ref bool claimsValid)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (AuthRoleCodec.TryDecode(value, out var role))
            return role;

        claimsValid = false;
        return null;
    }

    private static BotHostChoice? DecodeOptionalHost(string? value, ref bool claimsValid)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (BotHostClaimCodec.Decode(value) is { } host)
            return host;

        claimsValid = false;
        return null;
    }

    private static bool BooleanClaim(ClaimsPrincipal user, string claimType) =>
        string.Equals(user.FindFirstValue(claimType), "true", StringComparison.OrdinalIgnoreCase);
}
