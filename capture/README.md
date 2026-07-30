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
```

Viset evaluates every theme/device/view matrix item independently. Each item starts a fresh Release
Simulation process inside the Lua definition, waits for `/simulation/ready`, follows the real
`/simulation/login` alias, waits for route-specific visible ready-state content, captures the whole
page without hiding sibling sections, and stops that process even when capture fails.

The Shoutouts still capture opens the real **Automatic raid shoutouts** disclosure before snapshotting.
The animated captures use real page controls and scrolling. Generated files go directly to
`../src/BlokeBot.Site/wwwroot/media`.

## Editor support

`.luarc.json` loads `.viset/viset.d.lua` for Viset API completion and diagnostics in Lua Language
Server. For optional Neovim Tree-sitter highlighting of the TOML header and `viset.javascript`
regions, add `.viset/nvim` to `runtimepath` and install the Lua, TOML and JavaScript parsers.
