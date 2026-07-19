# Viset capture project

Edit `capture.lua`, then run:

```sh
viset capture capture.lua
```

Generated output: [`output/example.png`](output/example.png)

![Generated Viset capture](output/example.png)

Capture files are trusted local Lua code and run with Lua's standard libraries.

## Editor support

`.luarc.json` loads `.viset/viset.d.lua` for Viset API completion and diagnostics in Lua Language Server.

For optional Neovim Tree-sitter highlighting of the TOML header and `viset.javascript` regions, add `.viset/nvim` to `runtimepath` and install the Lua, TOML, and JavaScript parsers.

VS Code uses the LuaLS definitions but requires a separate compatible extension for embedded-language highlighting; it cannot consume the Tree-sitter query directly.
