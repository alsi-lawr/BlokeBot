<div align="center">

# Alsi.TwitchBot

A small `net10.0` library for hosting a Twitch IRC bot inside a .NET application.

![Target framework](https://img.shields.io/badge/target-net10.0-512BD4?logo=dotnet)
![Twitch](https://img.shields.io/badge/platform-Twitch-9146FF?logo=twitch&logoColor=white)

</div>

## Overview

`Alsi.TwitchBot` provides the reusable pieces of a Twitch chat bot: hosted IRC connection management, command dispatch, OAuth callback support, token refresh, and JSON token storage.

Application code still owns the things that make a bot specific: HTTP routes, command modules, filters, persistence, and any domain behavior.

## Install

This library is currently consumed as a source project in this repository.

It is currently referenced by the active `src/BlokeBot/BlokeBot.csproj` application.

## Usage

Register the hosted bot and add commands from your application:

```csharp
using Alsi.TwitchBot;

builder.Services
    .AddTwitchBot(builder.Configuration.GetSection("TwitchBot"))
    .AddCommandModule<MyCommandModule>();
```

```csharp
using Alsi.TwitchBot;

public sealed class MyCommandModule : ITwitchCommandModule
{
    public void AddCommands(ITwitchCommandBuilder commands)
    {
        commands.Map(
            "ping",
            async (ctx, args, ct) => await ctx.ReplyAsync("pong", ct)
        );
    }
}
```

## Configuration

Bind settings from a `TwitchBot` section:

```json
{
  "TwitchBot": {
    "StartupMessage": "Beep boop.",
    "Connection": {
      "Host": "irc.chat.twitch.tv",
      "Port": 6667,
      "UseTls": false
    },
    "Identity": {
      "BotUsername": "your-bot-login",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "RedirectUri": "https://example.com/twitch/oauth/callback",
      "Scopes": ["chat:read", "chat:edit"],
      "TokenCachePath": "twitch.tokens.json"
    }
  }
}
```

Register an `ITwitchBotChannelProvider` in the consuming app to provide the channel logins the runtime should connect to.

## OAuth

The library handles OAuth state validation, code exchange, token persistence, validation, and refresh. The consuming app provides the HTTP endpoints:

```csharp
app.MapGet(
    "/oauth/start",
    (ITwitchOAuthFlow oauth) => Results.Redirect(oauth.CreateAuthorizationUri().ToString())
);

app.MapGet(
    "/oauth/callback",
    async (string code, string state, ITwitchOAuthFlow oauth, CancellationToken ct) =>
    {
        await oauth.CompleteAuthorizationAsync(code, state, ct);
        return Results.Text("OK. Tokens saved.");
    }
);
```

## Build

```bash
dotnet build src/Alsi.TwitchBot/Alsi.TwitchBot.csproj
```

Run the current test project through its TUnit entrypoint:

```bash
dotnet run --project tests/Alsi.TwitchBot.Tests/Alsi.TwitchBot.Tests.csproj
```

> [!NOTE]
> With the .NET 10 SDK and Microsoft.Testing.Platform-based TUnit project, `dotnet test` may require opting into the new test experience.

## Status

- Twitch IRC and EventSub/Helix chat runtimes use an app-provided channel source.
- HTTP endpoints stay in the consuming app.
- Default token persistence is JSON file based.
- No license file is currently included in this repository.
