# BlokeBot server setup

1. Extract the archive into its own directory and keep every extracted file together.
2. Run `blokebot help` (`blokebot.exe help` on Windows) to review configuration and platform state locations.
3. Configure `BotUsername`, `ClientId`, `ClientSecret`, and `RedirectUri` through the environment names shown by the help command. Keep credentials outside the extracted directory.
4. Start BlokeBot with `blokebot serve --data-dir PATH`. Add `--urls http://127.0.0.1:8080` when a reverse proxy will provide public HTTPS.
5. Give only the service account access to the data directory. Back up `blokebot.db` and `twitch.tokens.json` together, and never publish either file.

The Twitch application's registered callback must exactly match the configured redirect URI. See the [Server Owner Guide](https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide) for reverse proxy, service, update, and recovery guidance.
