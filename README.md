# BlokeBot

BlokeBot is a self-hosted Twitch bot and Blazor administration dashboard for running channel
commands, guessing games, points, giveaways, and chat delivery from one application.

It lets a bot administrator add hosted channels and configure each channel through the dashboard.
The application persists its own SQLite state and uses Twitch OAuth for administrator and bot
authorisation; it does not provide a hosted service.

## Capabilities

- Hosted-channel administration, operator access, and channel-level bot authorisation.
- Built-in and custom commands, including command aliases and configurable announcements.
- Guessing rounds, profiles, history, and leaderboards.
- Points balances, gambling, scheduled giveaways, and points leaderboards.
- Twitch OAuth for dashboard and bot accounts.
- Resilient IRC and EventSub runtime handling, with public-chat delivery and private-response
  fallbacks where configured.

## Status and limitations

BlokeBot is pre-release software. Review the configuration, Twitch application permissions, and
operational controls before using it for a live channel. Twitch credentials and tokens are sensitive
state; keep them outside the repository and back them up only through an appropriate secure process.

No licence is currently declared for this repository. No permission to use, redistribute, or modify
the project is inferred from this README.

## Run from source

### Prerequisites

- .NET SDK `10.0.301` (pinned in [`global.json`](global.json)).
- Node.js and npm (the frontend dependency graph is locked in
  [`src/BlokeBot/package-lock.json`](src/BlokeBot/package-lock.json)).

Restore, build, and start the application:

```console
npm ci --prefix src/BlokeBot
dotnet restore BlokeBot.slnx --disable-parallel
dotnet build BlokeBot.slnx --no-restore --disable-parallel
dotnet run --project src/BlokeBot/BlokeBot.csproj --no-build
```

The project build also runs the locked frontend install and Tailwind build. For a development CSS
watcher, run `npm run css:watch --prefix src/BlokeBot` separately.

The application listens on the ASP.NET Core URL configuration. For a non-development HTTP listener:

```console
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://127.0.0.1:8080 \
  dotnet run --project src/BlokeBot/BlokeBot.csproj
```

## Configuration and state

[`src/BlokeBot/appsettings.json`](src/BlokeBot/appsettings.json) contains non-secret defaults and
the complete configuration shape. Configure secrets with standard ASP.NET Core environment variable
names (nested keys use `__`) or mount an `appsettings.Production.json` file beside the executable.
For example:

```console
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://+:8080
export BlokeBot__DatabasePath=/absolute/path/to/data/blokebot.db
export TwitchBot__Identity__TokenCachePath=/absolute/path/to/data/twitch.tokens.json
export TwitchWebAuth__ClientId='<dashboard-twitch-client-id>'
export TwitchWebAuth__ClientSecret='<dashboard-twitch-client-secret>'
export TwitchBot__Identity__BotUsername='<bot-login-name>'
export TwitchBot__Identity__ClientId='<bot-twitch-client-id>'
export TwitchBot__Identity__ClientSecret='<bot-twitch-client-secret>'
export TwitchBot__Identity__RedirectUri='https://<public-host>/oauth/callback'
```

`BlokeBot__DatabasePath` is SQLite state. `TwitchBot__Identity__TokenCachePath` is the bot OAuth
token cache. Set both to a durable, private directory; the persistence service creates the database
directory if needed. The source defaults are relative paths for local development. When bot identity
settings are absent, the bot runtime remains offline; configuring them enables its Twitch runtime.

Dashboard OAuth settings and bot identity settings are distinct. Use placeholders or externally
managed configuration only—do not commit client secrets, token caches, or SQLite files.

## Docker

Build the local image:

```console
docker build --tag blokebot:local .
```

Run it with durable state mounted at `/data`:

```console
mkdir -p data
docker run --rm --init --publish 8080:8080 --volume "$PWD/data:/data" \
  --env TwitchWebAuth__ClientId='<dashboard-twitch-client-id>' \
  --env TwitchWebAuth__ClientSecret='<dashboard-twitch-client-secret>' \
  --env TwitchBot__Identity__BotUsername='<bot-login-name>' \
  --env TwitchBot__Identity__ClientId='<bot-twitch-client-id>' \
  --env TwitchBot__Identity__ClientSecret='<bot-twitch-client-secret>' \
  --env TwitchBot__Identity__RedirectUri='https://<public-host>/oauth/callback' \
  blokebot:local
```

The image is a multi-stage build: it installs frontend dependencies from `package-lock.json`, builds
the .NET application pinned by `global.json` and `Directory.Packages.props`, runs on ASP.NET 10, and
uses its non-root `app` user. Its defaults bind HTTP on port `8080` and place both mutable state files
under `/data`.
Mount a production configuration file at `/app/appsettings.Production.json` when environment
variables are not suitable. The build context excludes development settings, token caches,
databases, build output, and node modules.

## Nix (Linux x86_64)

The locked flake exposes the default package and application for `x86_64-linux`:

```console
nix build .#
./result/bin/BlokeBot

nix run .#
```

The Nix wrapper deliberately does not set credentials or mutable-state locations. Supply the same
ASP.NET Core environment variables shown above and select a writable database and token-cache
directory before running it.

## Development checks

Formatting is managed by the pinned CSharpier tool:

```console
dotnet tool restore
dotnet csharpier check .
dotnet csharpier format .
```

Run the Microsoft Testing Platform suite when you intend to run all tests:

```console
dotnet restore BlokeBot.slnx --disable-parallel
dotnet test BlokeBot.slnx --no-restore --disable-parallel -v:minimal
```

[`CONTRIBUTING.md`](CONTRIBUTING.md) covers the formatter policy, and
[`tests/README.md`](tests/README.md) describes the test-project boundaries. The implementation
notes in [`docs/`](docs/) are focused reference documentation rather than deployment guidance.
