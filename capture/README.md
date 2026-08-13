# Dashboard help media

The capture definitions render the deterministic `BlokeBot.Simulation` fixture used by the help
site. Each definition records its own routes, themes, devices and readiness conditions.

## Regenerate

Build Simulation once, then use the local Viset checkout. Viset is screen capture tooling only;
these runs do not verify product behaviour.

```sh
dotnet build ../src/BlokeBot.Simulation/BlokeBot.Simulation.csproj \
  --configuration Release --property:TreatWarningsAsErrors=true --nologo
```

Use `./capture-all.sh` to regenerate everything. It starts one Simulation per definition and
leaves it up for that definition's whole matrix, rather than paying a fresh Simulation start for
every theme, device and view. Pass a single definition name to run just that one:

```sh
./capture-all.sh                       # every definition
./capture-all.sh chat-tools-switches.lua
```

Each definition reuses a Simulation already listening on its port and only starts its own when
none is running, so the individual commands below still work unchanged.

From this directory, point `VISET_CHECKOUT` at the local Viset checkout, then run each
definition on its own unused loopback port. Port `5084` is reserved for human
visual signoff and must not be used or stopped by capture work.

```sh
VISET_CHECKOUT="${VISET_CHECKOUT:-../../Viset}"
BLOKEBOT_DASHBOARD_PORT=43217 nix run "$VISET_CHECKOUT" -- capture dashboard-and-admin.lua --force
BLOKEBOT_HOME_SCROLL_PORT=43218 nix run "$VISET_CHECKOUT" -- capture home-scroll.lua --force
BLOKEBOT_GUESSING_CAPTURE_PORT=43219 nix run "$VISET_CHECKOUT" -- capture guessing-workflow.lua --force
BLOKEBOT_CUSTOM_COMMANDS_PORT=43220 nix run "$VISET_CHECKOUT" -- capture custom-commands.lua --force
BLOKEBOT_AUTOMATION_EVENTS_PORT=43221 nix run "$VISET_CHECKOUT" -- capture automation-events.lua --force
BLOKEBOT_POINTS_GUESSING_PORT=43222 nix run "$VISET_CHECKOUT" -- capture points-and-guessing.lua --force
BLOKEBOT_NATIVE_TWITCH_PORT=43223 nix run "$VISET_CHECKOUT" -- capture native-twitch-operations.lua --force
BLOKEBOT_VIEWER_COMMAND_CATALOG_PORT=5334 nix run "$VISET_CHECKOUT" -- capture viewer-command-catalog.lua --force
BLOKEBOT_CHAT_TOOLS_PORT=5335 nix run "$VISET_CHECKOUT" -- capture chat-tools-switches.lua --force
BLOKEBOT_COMMUNITY_GUIDES_PORT=5460 nix run "$VISET_CHECKOUT" -- capture community-guides.lua --force
BLOKEBOT_OVERLAY_SOURCES_PORT=5461 nix run "$VISET_CHECKOUT" -- capture overlay-sources.lua --force
BLOKEBOT_OVERLAY_PREVIEWS_PORT=5462 nix run "$VISET_CHECKOUT" -- capture overlay-previews.lua --force
BLOKEBOT_V010_FIGURES_PHONE_PORT=5473 nix run "$VISET_CHECKOUT" -- capture v010-guide-figures-phone.lua --force
BLOKEBOT_V010_FIGURES_LAPTOP_PORT=5475 nix run "$VISET_CHECKOUT" -- capture v010-guide-figures-laptop.lua --force
BLOKEBOT_PROGRESSION_LAPTOP_PORT=5476 nix run "$VISET_CHECKOUT" -- capture community-progression-figures-laptop.lua --force
BLOKEBOT_PROGRESSION_PHONE_PORT=5477 nix run "$VISET_CHECKOUT" -- capture community-progression-figures-phone.lua --force
```

The definitions have disjoint output names and ports, so they may run in parallel. Viset evaluates
every theme/device/view matrix item independently. Each item starts a fresh Release Simulation
process inside the Lua definition, waits for `/simulation/ready`, follows the real
`/simulation/login` alias, waits only for the route to load and its `main` element to finish fading
in, captures the page without hiding sibling sections, and stops that process even when capture
fails. The definitions assert nothing about product state; they set up a deterministic fixture and
take the picture.

Each definition covers one guide area so a single area can be recaptured without rerunning the
others. `chat-tools-switches.lua` and `viewer-command-catalog.lua` drive the deterministic round,
giveaway, feature and stream-liveness endpoints first: the former captures the all-disabled and
representative enabled Chat Tools states, the latter opens the **Available viewer commands**
disclosure. `community-guides.lua` captures the current moderator workspace on laptops and the
matching participant view on phones for request boards, play-with-viewers queues and moments. The
Simulation fixture provides the approved, voted moment shown in both moments captures.
`overlay-sources.lua` and `overlay-previews.lua` capture Browser Sources, Cues, Media, Guessing,
active Giveaway, Event feed and Viewer Queue in light and dark laptop and phone frames without
exposing a private Browser Source URL. The animated captures use PNG screencast frames, high-quality lossy WebP output, real
page controls and scrolling. Phone home-scroll captures show a touch-contact circle during each
gesture.

The two `v010-guide-figures-*` definitions produce the guide figures under
`media/community/v010`. They are split by device because each figure pins one theme and one device
rather than a full matrix. The two `community-progression-figures-*` definitions produce the Bingo,
bounty and season figures under `media/community/progression`: the laptop set covers the moderator
setup, moderation and archive areas, and the phone set covers the disabled-feature recovery states
and the public card, board and season pages.

Generated files go directly to `../src/BlokeBot.Site/wwwroot/media`; do not hand-edit them.

## Editor support

`.luarc.json` loads `.viset/viset.d.lua` for Viset API completion and diagnostics in Lua Language
Server. For optional Neovim Tree-sitter highlighting of the TOML header and `viset.javascript`
regions, add `.viset/nvim` to `runtimepath` and install the Lua, TOML and JavaScript parsers.
