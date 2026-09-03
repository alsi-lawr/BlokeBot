# Dashboard help media

Viset capture definitions that photograph the deterministic `BlokeBot.Simulation` fixture for the
help site. They set up a fixture state and take the picture; they assert nothing about product
behaviour, so a definition passing does not mean the product is correct.

## Regenerate

Build Simulation once:

```sh
dotnet build ../src/BlokeBot.Simulation/BlokeBot.Simulation.csproj \
  --configuration Release --property:TreatWarningsAsErrors=true --nologo
```

Then run the definitions. `capture-all.sh` owns the definition-to-port mapping and starts one
Simulation per definition, holding it up for that definition's whole matrix:

```sh
./capture-all.sh                          # every definition
./capture-all.sh chat-tools-switches.lua  # just one
CAPTURE_JOBS=2 ./capture-all.sh           # override concurrency
```

Concurrency defaults to a quarter of the cores, capped at six, since each definition runs its own
Simulation and browser. Lower it if browsers are killed mid-navigate.

A definition reuses a Simulation already listening on its port and starts its own only when none
is, so running one directly still works:

```sh
BLOKEBOT_CAPTURE_PORT=5335 nix run ../../Viset -- capture chat-tools-switches.lua --force
```

Port `5084` is reserved for human visual signoff and must not be used or stopped by capture work.

## How a capture behaves

Each matrix item waits for `/simulation/started`, follows the real `/simulation/login` alias, sets
the fixture state it needs, waits for the route and its `main` element to settle with no Blazor
reconnect showing, then snapshots without hiding sibling sections.

`/simulation/started` latches once the fixture has fully wired and stays true. `/simulation/ready`
reports *live* EventSub wiring, which any capture that disables a feature tears down, so it cannot
gate a whole matrix.

Definitions that change fixture state wait for `/simulation/ready` again afterwards, so the app has
finished reconnecting before the picture is taken.

## Output

Files go to an area subdirectory of `../src/BlokeBot.Site/wwwroot/media`: `dashboard`,
`chat-tools`, `commands`, `points-and-guessing`, `native-twitch`, `overlays`, `community`,
`community/figures` and `community/progression`. Do not hand-edit them.

Laptop frames are 1920x1080 and phone frames 495x1100, both before Viset's device chrome.

## Design evidence

`viewer-portal-mockup.lua` and `viewer-portal-mockup-phone-lower.lua` photograph the
Simulation-only viewer portal mockup at `/simulation/portal-mockup` for the BLOKEBOT-274 design
gate. They write into the planning store
(`agent-planning/.../20260901-milestone-015-v0.15.0-viewer-portal/evidence/BLOKEBOT-274/captures`),
not into help media, so `capture-all.sh` does not list them. Run them directly:

```sh
BLOKEBOT_CAPTURE_PORT=5610 nix run ../../Viset -- capture viewer-portal-mockup.lua --force
BLOKEBOT_CAPTURE_PORT=5610 nix run ../../Viset -- capture viewer-portal-mockup-phone-lower.lua --force
```

## Editor support

`.luarc.json` loads `.viset/viset.d.lua` for Viset API completion and diagnostics in Lua Language
Server. For optional Neovim Tree-sitter highlighting of the TOML header and `viset.javascript`
regions, add `.viset/nvim` to `runtimepath` and install the Lua, TOML and JavaScript parsers.
