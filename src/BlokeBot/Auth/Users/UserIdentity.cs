using BlokeBot.Functional;

namespace BlokeBot.Auth.Users;

internal sealed record UserIdentity
{
    private UserIdentity(string id, string login, string displayName, string profileImageUrl)
    {
        Id = id;
        Login = login;
        DisplayName = displayName;
        ProfileImageUrl = profileImageUrl;
    }

    public string Id { get; }

    public string Login { get; }

    public string DisplayName { get; }

    public string ProfileImageUrl { get; }

    internal static Option<UserIdentity> Create(
        string id,
        string login,
        string displayName,
        string profileImageUrl
    )
    {
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(login)
            ? Option<UserIdentity>.None
            : Option<UserIdentity>.Some(new UserIdentity(id, login, displayName, profileImageUrl));
    }
}
