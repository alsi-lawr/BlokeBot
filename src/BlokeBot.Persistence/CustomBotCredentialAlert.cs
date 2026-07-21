namespace BlokeBot.Persistence;

public static class CustomBotCredentialAlert
{
    public const string Source = "custom-bot-credentials";
    public const string SourceKey = "credentials-removed-v1";
    public const string Title = "Reconnect your custom bot";
    public const string Message =
        "BlokeBot removed custom bot credentials it could not safely use. Select the custom bot and connect it again.";
    public const string LinkPath = "/host";
}
