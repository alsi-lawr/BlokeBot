# Dashboard help media

The capture definitions render the deterministic `BlokeBot.Simulation` fixture used by the help site.
`media-manifest.json` is the source of truth for every generated PNG and WebP: it records the real
route, fresh-process scenario, theme, device, expected framed dimensions and semantic readiness.

## Regenerate

Install the pinned Viset version named by the manifest, then build Simulation once:

```sh
dotnet build ../src/BlokeBot.Simulation/BlokeBot.Simulation.csproj \
  --configuration Release --property:TreatWarningsAsErrors=true --nologo
```

Run each definition on its own unused loopback port. Port `5084` is reserved for human visual signoff
and must not be used or stopped by capture work.

```sh
BLOKEBOT_SCREENSHOT_PORT=43217 viset capture screenshots.lua
BLOKEBOT_HOME_SCROLL_PORT=43218 viset capture home-scroll.lua
BLOKEBOT_GUESSING_CAPTURE_PORT=43219 viset capture guessing-workflow.lua
BLOKEBOT_V05_GUIDES_PORT=5334 viset capture v0.5-guides.lua
BLOKEBOT_COMMUNITY_GUIDES_PORT=5460 viset capture community-guides.lua
```

Viset evaluates every theme/device/view matrix item independently. Each item starts a fresh Release
Simulation process inside the Lua definition, waits for `/simulation/ready`, follows the real
`/simulation/login` alias, waits for route-specific visible ready-state content, captures the whole
page without hiding sibling sections, and stops that process even when capture fails.

The Shoutouts still capture opens the real **Automatic raid shoutouts** disclosure before
snapshotting. Before every state-dependent capture, `v0.5-guides.lua` drives the deterministic
round, giveaway, feature and stream-liveness endpoints. It captures both all-disabled and
representative enabled Chat Tools states and opens the real **Available viewer commands**
disclosure. `community-guides.lua` captures the current
moderator workspace on laptops and the matching participant view on phones for request boards,
play-with-viewers queues and moments. The Simulation fixture provides the approved, voted moment
shown in both moments captures. The animated captures use real page controls and scrolling.
Generated files go directly to
`../src/BlokeBot.Site/wwwroot/media`; do not hand-edit them.

## Editor support

`.luarc.json` loads `.viset/viset.d.lua` for Viset API completion and diagnostics in Lua Language
Server. For optional Neovim Tree-sitter highlighting of the TOML header and `viset.javascript`
regions, add `.viset/nvim` to `runtimepath` and install the Lua, TOML and JavaScript parsers.
