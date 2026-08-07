# Dashboard help media

The capture definitions render the deterministic `BlokeBot.Simulation` fixture used by the help
site. `media-manifest.json` is the source of truth for every generated PNG and WebP: it records the
real route, fresh-process scenario, theme, device, expected framed dimensions and semantic
readiness.

## Regenerate

Build Simulation once, then use the local Viset checkout. Viset is screen capture tooling only;
these runs do not verify product behaviour.

```sh
dotnet build ../src/BlokeBot.Simulation/BlokeBot.Simulation.csproj \
  --configuration Release --property:TreatWarningsAsErrors=true --nologo
```

From this directory, point `VISET_CHECKOUT` at the local Viset checkout, then run all six
manifest-owned definitions on their own unused loopback ports. Port `5084` is reserved for human
visual signoff and must not be used or stopped by capture work.

```sh
VISET_CHECKOUT="${VISET_CHECKOUT:-../../Viset}"
BLOKEBOT_SCREENSHOT_PORT=43217 nix run "$VISET_CHECKOUT" -- capture screenshots.lua --force
BLOKEBOT_HOME_SCROLL_PORT=43218 nix run "$VISET_CHECKOUT" -- capture home-scroll.lua --force
BLOKEBOT_GUESSING_CAPTURE_PORT=43219 nix run "$VISET_CHECKOUT" -- capture guessing-workflow.lua --force
BLOKEBOT_V05_GUIDES_PORT=5334 nix run "$VISET_CHECKOUT" -- capture v0.5-guides.lua --force
BLOKEBOT_COMMUNITY_GUIDES_PORT=5460 nix run "$VISET_CHECKOUT" -- capture community-guides.lua --force
BLOKEBOT_V06_OVERLAY_GUIDES_PORT=5461 nix run "$VISET_CHECKOUT" -- capture v0.6-overlay-guides.lua --force
```

The definitions have disjoint output names and ports, so they may run in parallel. Viset evaluates
every theme/device/view matrix item independently. Each item starts a fresh Release Simulation
process inside the Lua definition, waits for `/simulation/ready`, follows the real
`/simulation/login` alias, waits for route-specific visible ready-state content, captures the page
without hiding sibling sections, and stops that process even when capture fails.

The Shoutouts still capture opens the real **Automatic raid shoutouts** disclosure before
snapshotting, and the Twitch events capture waits for all 21 automation event sources to report
ready. Before every state-dependent capture, `v0.5-guides.lua` drives the deterministic
round, giveaway, feature and stream-liveness endpoints. It captures both all-disabled and
representative enabled Chat Tools states and opens the real **Available viewer commands**
disclosure. `community-guides.lua` captures the current moderator workspace on laptops and the
matching participant view on phones for request boards, play-with-viewers queues and moments. The
Simulation fixture provides the approved, voted moment shown in both moments captures.
`v0.6-overlay-guides.lua` captures Browser Sources, Guessing, active Giveaway, Event feed, Viewer
Queue, Cues and Media in light and dark laptop and phone frames without exposing a private Browser
Source URL. The animated captures use real page controls and scrolling.

The final matrix is 108 generated assets: 100 PNG and 8 WebP. `twitch-operations.lua` is not
manifest-owned and is not part of this regeneration. Generated files go directly to
`../src/BlokeBot.Site/wwwroot/media`; do not hand-edit them.

## Editor support

`.luarc.json` loads `.viset/viset.d.lua` for Viset API completion and diagnostics in Lua Language
Server. For optional Neovim Tree-sitter highlighting of the TOML header and `viset.javascript`
regions, add `.viset/nvim` to `runtimepath` and install the Lua, TOML and JavaScript parsers.
