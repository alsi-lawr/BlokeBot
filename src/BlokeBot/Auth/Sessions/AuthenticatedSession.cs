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
    public string Role { get; init; } = string.Empty;
    public BotHostSelection? HostSelection { get; init; }
    public AuthSessionHostSelectionState HostSelectionState { get; init; }
    public string? AdminEditingLogin { get; init; }
    public BotHostChoice? AdminReturnHost { get; init; }

    public bool IsAdminEditing => !string.IsNullOrWhiteSpace(AdminEditingLogin);

    public string DisplayRole =>
        IsBotAccount ? AuthRole.Bot
        : HostSelection?.Current.Role
        ?? (string.IsNullOrWhiteSpace(Role) ? "operator" : Role);

    public string DisplayText =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(Login) ? Login
        : "Twitch user";

    public bool HasCapability(AuthSessionCapability capability) =>
        capability switch
        {
            AuthSessionCapability.BotAdmin => IsBotAdmin,
            AuthSessionCapability.HostSelected => HostSelection is not null,
            AuthSessionCapability.Operator =>
                HostSelection is not null
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

        return HostSelection is null
            ? CanCreateHost
            : existingHostIds.Contains(HostSelection.Current.Id)
                && CurrentHostRoleIs(AuthRole.Streamer);
    }

    public bool CanUseBotFunctions(IReadOnlySet<int> existingHostIds) =>
        !IsBotAccount
        && HostSelection is not null
        && existingHostIds.Contains(HostSelection.Current.Id);

    public bool CanAuthorizeSelectedHost =>
        HostSelection is not null
        && CurrentHostRoleIs(AuthRole.Streamer)
        && string.Equals(
            HostSelection.Current.Login,
            Login,
            StringComparison.OrdinalIgnoreCase
        );

    public bool CurrentHostRoleIs(string role) =>
        HostSelection is not null
        && string.Equals(HostSelection.Current.Role, role, StringComparison.OrdinalIgnoreCase);

    public static AuthenticatedSession FromPrincipal(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return Anonymous;

        var availableHosts = user
            .FindAll(BotHostClaims.AvailableHost)
            .Select(claim => BotHostClaimCodec.Decode(claim.Value))
            .OfType<BotHostChoice>()
            .OrderBy(host => host.DisplayName)
            .ToArray();

        var hostSelection = ParseHostSelection(user, availableHosts, out var hostSelectionState);
        var adminReturnHost = user.FindFirstValue(BotHostClaims.AdminReturnHost) is { } encoded
            && !string.IsNullOrWhiteSpace(encoded)
            ? BotHostClaimCodec.Decode(encoded)
            : null;

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
            Role = user.FindFirstValue(AuthClaims.Role) ?? string.Empty,
            HostSelection = hostSelection,
            HostSelectionState = hostSelectionState,
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
        if (availableHosts.Count == 0)
        {
            state = AuthSessionHostSelectionState.None;
            return null;
        }

        if (!int.TryParse(user.FindFirstValue(BotHostClaims.SelectedHostId), out var selectedId))
        {
            state = AuthSessionHostSelectionState.Invalid;
            return null;
        }

        var current = availableHosts.FirstOrDefault(host => host.Id == selectedId);
        if (current is null)
        {
            state = AuthSessionHostSelectionState.Invalid;
            return null;
        }

        state = AuthSessionHostSelectionState.Selected;
        return new BotHostSelection(current, availableHosts);
    }

    private static bool BooleanClaim(ClaimsPrincipal user, string claimType) =>
        string.Equals(
            user.FindFirstValue(claimType),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
}
