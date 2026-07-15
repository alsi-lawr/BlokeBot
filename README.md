<div align="center">

# BlokeBot

**A self-hosted Twitch bot and Blazor admin dashboard for commands, guessing games, points,
giveaways, and resilient chat delivery.**

Run the bot and manage its hosted channels from one application, while keeping credentials and
channel data under your control.

</div>

## What BlokeBot does

BlokeBot gives bot administrators a web dashboard for adding hosted Twitch channels and managing
each channel's features. It persists its own SQLite state, uses Twitch OAuth for administrator and
bot authorisation, and is designed to be run on your own infrastructure rather than as a hosted
service.

- Manage hosted channels, operator access, and channel-level bot authorisation.
- Run built-in and custom commands, with aliases and configurable announcements.
- Create guessing rounds and profiles, and review history and leaderboards.
- Track points balances, gambling, scheduled giveaways, and points leaderboards.
- Authorise dashboard users and bot accounts through separate Twitch OAuth settings.
- Handle IRC and EventSub runtime recovery, with durable public-chat delivery and redacted
  private-response failure handling.

## Get started

### Docker

Build the local image:

```console
docker build --tag blokebot:local .
```

Run it with a named volume for durable application state:

```console
docker run --rm --init --publish 8080:8080 --volume blokebot-data:/data \
  --env TwitchBot__Identity__BotUsername='<bot-login-name>' \
  --env TwitchBot__Identity__ClientId='<bot-twitch-client-id>' \
  --env TwitchBot__Identity__ClientSecret='<bot-twitch-client-secret>' \
  --env TwitchBot__Identity__RedirectUri='https://<public-host>/oauth/callback' \
  blokebot:local
```

Open `http://localhost:8080` after the container starts. The image defaults to port `8080` and
stores its SQLite database and bot OAuth token cache under `/data`. A named volume is recommended
because the image runs as its non-root `app` user; if you use a bind mount instead, its host
directory must be writable by that user.

The multi-stage image installs the locked frontend dependencies, builds the .NET application using
the SDK pinned by [`global.json`](global.json), and runs it on ASP.NET 10. Mount a production
configuration file at `/app/appsettings.Production.json` when environment variables are not
suitable. The build context excludes development settings, token caches, databases, build output,
and node modules.

### From source

#### Prerequisites

- .NET SDK `10.0.301`, pinned in [`global.json`](global.json).
- Node.js and npm; the frontend dependency graph is locked in
  [`src/BlokeBot/package-lock.json`](src/BlokeBot/package-lock.json).

Restore, build, and start the application:

```console
npm ci --prefix src/BlokeBot
dotnet restore BlokeBot.slnx --disable-parallel
dotnet build BlokeBot.slnx --no-restore --disable-parallel
dotnet run --project src/BlokeBot/BlokeBot.csproj --no-build
```

The project build also runs the locked frontend install and Tailwind build. To rebuild CSS while
developing, run `npm run css:watch --prefix src/BlokeBot` separately.

The application listens on the standard ASP.NET Core URL configuration. For a non-development HTTP
listener:

```console
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://127.0.0.1:8080 \
  dotnet run --project src/BlokeBot/BlokeBot.csproj
```

### Nix (Linux x86_64 and ARM64)

The locked flake exposes an installable package, runnable application, development shell, and NixOS
module for `x86_64-linux` and `aarch64-linux`:

```console
nix build .#
./result/bin/BlokeBot

nix run .#
nix profile install .#blokebot
nix develop
```

The package wrapper locates its immutable application configuration automatically. It deliberately
does not set credentials or mutable-state locations when run directly; supply the same ASP.NET Core
environment variables described below and choose writable database and token-cache paths.

For a declarative NixOS installation, add this flake as an input and import its module:

```nix
{
  inputs.blokebot.url = "github:alsi-lawr/BlokeBot";

  outputs =
    { blokebot, nixpkgs, ... }:
    {
      nixosConfigurations.my-host = nixpkgs.lib.nixosSystem {
        system = "x86_64-linux"; # Or "aarch64-linux" for ARM64.
        modules = [
          blokebot.nixosModules.default
          {
            services.blokebot = {
              enable = true;
              listenAddress = "0.0.0.0";
              openFirewall = true;
              environment = {
                TwitchBot__Identity__BotUsername = "my-bot";
                TwitchBot__Identity__ClientId = "public-client-id";
                TwitchBot__Identity__RedirectUri = "https://bot.example.com/oauth/callback";
              };
              environmentFile = "/run/secrets/blokebot.env";
            };
          }
        ];
      };
    };
}
```

Put secrets in the systemd environment file rather than in the Nix configuration:

```shell
TwitchBot__Identity__ClientSecret=private-client-secret
```

The module runs BlokeBot as a dedicated system user, stores its SQLite database and OAuth token
cache under `/var/lib/blokebot`, and listens on `127.0.0.1:8080` unless configured otherwise. It
leaves the firewall closed by default.

## Configuration and state

[`src/BlokeBot/appsettings.json`](src/BlokeBot/appsettings.json) contains the non-secret defaults
and configuration shape. Configure secrets with standard ASP.NET Core environment variable names,
where nested keys use `__`, or place an `appsettings.Production.json` file beside the executable.

```console
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://+:8080
export BlokeBot__DatabasePath=/absolute/path/to/data/blokebot.db
export TwitchBot__Identity__TokenCachePath=/absolute/path/to/data/twitch.tokens.json
export TwitchBot__Identity__BotUsername='<bot-login-name>'
export TwitchBot__Identity__ClientId='<bot-twitch-client-id>'
export TwitchBot__Identity__ClientSecret='<bot-twitch-client-secret>'
export TwitchBot__Identity__RedirectUri='https://<public-host>/oauth/callback'
```

`BlokeBot__DatabasePath` points to the SQLite state. `TwitchBot__Identity__TokenCachePath` points to
the bot OAuth token cache. Keep both in a durable, private directory; the persistence service
creates the database directory when needed. Their source defaults are relative paths intended for
local development.

Dashboard sign-in and bot OAuth share the Twitch application credentials under
`TwitchBot__Identity`; `TwitchWebAuth` contains only web-specific callback and cookie settings. When
bot identity settings are absent, the dashboard can still start while Twitch sign-in and the bot
runtime remain offline. Do not commit client secrets, token caches, or SQLite files; use
placeholders or externally managed configuration.

## Development checks

Formatting is managed by the pinned CSharpier tool:

```console
dotnet tool restore
dotnet csharpier check .
dotnet csharpier format .
```

Run the complete Microsoft Testing Platform suite with:

```console
dotnet restore BlokeBot.slnx --disable-parallel
dotnet test BlokeBot.slnx --no-restore --disable-parallel -v:minimal
```

[`CONTRIBUTING.md`](CONTRIBUTING.md) covers the formatter policy, and
[`tests/README.md`](tests/README.md) describes the test-project boundaries. Files under
[`docs/`](docs/) are focused implementation notes rather than deployment guidance.

## Project status

BlokeBot is pre-release software. Review its configuration, Twitch application permissions, and
operational controls before using it for a live channel. Twitch credentials and tokens are
sensitive state; keep them outside the repository and back them up only through an appropriate
secure process.

## Licence

No licence is currently declared for this repository. No permission to use, redistribute, or
modify the project is inferred from this README.
