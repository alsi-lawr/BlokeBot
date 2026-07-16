namespace BlokeBot.Core.Auth.Sessions;

internal static class AuthClaims
{
    public const string CanCreateHost = "blokebot:can-create-host";
    public const string Login = "blokebot:login";
    public const string ProfileImageUrl = "blokebot:profile-image-url";
    public const string Role = "blokebot:role";
    public const string IsBotAdmin = "blokebot:is-bot-admin";
    public const string IsBotAccount = "blokebot:is-bot-account";
}
