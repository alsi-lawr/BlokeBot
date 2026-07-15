<div align="center">

<img src="assets/blokebot-banner.svg" alt="BlokeBot — Own your channel tools" width="100%" />

[![Status: pre-release](https://img.shields.io/badge/status-pre--release-F59E0B?style=flat-square)](https://github.com/alsi-lawr/BlokeBot/wiki)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?style=flat-square&logo=docker&logoColor=white)](https://github.com/alsi-lawr/BlokeBot/wiki/Installation)
[![Nix](https://img.shields.io/badge/Nix-x86__64%20%7C%20ARM64-5277C3?style=flat-square&logo=nixos&logoColor=white)](https://github.com/alsi-lawr/BlokeBot/wiki/Install-with-Nix)

**A self-hosted Twitch bot and dashboard for running channel tools on your own terms.**

[Documentation](https://github.com/alsi-lawr/BlokeBot/wiki) ·
[Install](https://github.com/alsi-lawr/BlokeBot/wiki/Installation) ·
[Configure](https://github.com/alsi-lawr/BlokeBot/wiki/Configuration)

</div>

## Inside BlokeBot

- **Hosted channels** — one dashboard for channel access, bot authorisation, and runtime status.
- **Commands** — built-in and custom commands, aliases, cooldowns, and announcements.
- **Guessing games** — profiles, rounds, rewards, history, and leaderboards.
- **Points** — balances, gambling, scheduled giveaways, and public leaderboards.
- **Resilient chat** — EventSub and IRC operation with durable public-message delivery.
- **Private by default** — self-hosted SQLite state and credentials under your control.

## Quick start

### Docker

```console
docker build --tag blokebot .
docker run --rm --init --publish 8080:8080 --volume blokebot-data:/data blokebot
```

### Nix

```console
nix run github:alsi-lawr/BlokeBot
```

Open the dashboard, then follow the [configuration guide](https://github.com/alsi-lawr/BlokeBot/wiki/Configuration)
to connect Twitch and choose production settings.

Everything beyond the quick start lives in the **[BlokeBot Wiki](https://github.com/alsi-lawr/BlokeBot/wiki)**.
