<div align="center">

<img src="assets/blokebot-mark.svg" alt="BlokeBot logo" width="128" height="128" />

# BlokeBot

[![Build and test](https://github.com/alsi-lawr/BlokeBot/actions/workflows/build-test.yml/badge.svg)](https://github.com/alsi-lawr/BlokeBot/actions/workflows/build-test.yml)
[![Release](https://img.shields.io/github/v/release/alsi-lawr/BlokeBot?display_name=tag&sort=semver)](https://github.com/alsi-lawr/BlokeBot/releases/latest)

**A self-hosted Twitch bot and dashboard for running channel tools on your own terms.**

[Help site](https://www.blokebot.com/) |
[Open BlokeBot](https://bot.blokebot.com/) |
[Server owner guide](https://www.blokebot.com/server-owners)

</div>

## Inside BlokeBot

- **One dashboard** - switch between the Twitch channels you help manage.
- **Commands** - create replies, aliases, counters, cooldowns, and scheduled messages.
- **Guessing games** - run rounds, reward winners, and share leaderboards.
- **Points and giveaways** - manage balances, gambling, prizes, and public rankings.
- **Clear status** - see whether the bot is ready and what needs your attention.
- **Your own copy** - keep channel data and credentials under your control.

## New to BlokeBot?

If someone has already set up BlokeBot for your channel, you do not need Docker or Nix.

1. Open the BlokeBot address they gave you.
2. Sign in with Twitch.
3. Choose your channel.
4. Follow the dashboard prompts to connect the bot and turn on the tools you want.

The **[BlokeBot help site](https://www.blokebot.com/guide)** walks through each step in plain language.

## Run your own copy

### Docker

```console
docker build --file packaging/docker/blokebot.Dockerfile --tag blokebot .
docker run --rm --init --publish 8080:8080 --volume blokebot-data:/data blokebot
```

### Nix

```console
nix run github:alsi-lawr/BlokeBot#blokebot -- serve
```

Run the separate [BlokeBot.Site help site](src/BlokeBot.Site) for approachable setup guidance. The
public [help site](https://www.blokebot.com/) includes the
[server owner guide](https://www.blokebot.com/server-owners) for technical detail.

For everyday help, start with the **[BlokeBot user guide](https://www.blokebot.com/guide)**. The
hosted bot is available at **[bot.blokebot.com](https://bot.blokebot.com/)**.
