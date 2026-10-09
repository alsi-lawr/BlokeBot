# Trusted plugin contract

BlokeBot v0.13 plugins are curated, installation-wide Lua 5.4 packages. Installing a plugin trusts
its Lua code to the same operating-system account as BlokeBot. The plugin can use the full Lua 5.4
standard library. It can therefore reach files, processes, and networks that the BlokeBot account
can reach. BlokeBot does not present capability grants and does not describe this model as a
sandbox.

Each admitted plugin runs in a separate worker process. That process boundary limits the effect of
crashes and resource failures on the host. It is an availability boundary, not a security boundary.
A compatible engine must provide Lua 5.4, the full standard library, coroutine suspension and
resumption, cooperative cancellation, the canonical package policy, and the canonical typed value
and host API contracts. KeraLua is preferred, but an engine name does not bypass these fixtures.

## Package identity

A plugin has one stable plugin ID. A selected installation records the plugin's declared semantic
version and one Git tag. A tag must not be a commit hash. Manifests, derived marketplace
snapshots, and installation records do not have a commit-SHA identity field.

The curated repository layout is `plugins/<plugin-id>/plugin.toml`. The directory name must exactly
match the manifest ID. The manifest owns the author, search tags, optional presentation URLs,
release targets, version, tag, compatibility, and runtime declarations. There is no global
catalogue or generated index. BlokeBot enumerates the fixed repository layout and transactionally
replaces its local searchable snapshot only after every discovered manifest passes validation.

## Package content

The canonical package contains `plugin.toml`, declared `.lua` source modules, and any
marketplace-reviewed declared payloads. Payloads can include browser or media assets, native files,
.NET assemblies, WebAssembly, and other plugin-managed files. Every payload declares its path,
purpose, maximum size, and explicit supported BlokeBot release RIDs. The validator enforces target,
declaration, archive, path, collision, and link rules without classifying trusted payload bytes.
The supported RIDs are `linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`, and `win-arm64`.

Lua modules are the only BlokeBot-managed entrypoints. Other payloads are available only for the
trusted plugin's own use; BlokeBot does not load them as managed plugin entrypoints or resolve their
external dependencies. Undeclared files, path escapes, case-colliding paths, and links are rejected.

## Host boundary

The host API exchanges closed typed values and outcomes. Calls identify their host module,
operation, coroutine, and installation, channel, automation, migration, or page context. A host call
can complete with a value, a typed failure, or cancellation. Asynchronous host work suspends only
the originating Lua coroutine and resumes it at most once. Every host-call wait is cancellable; an
operation does not declare optional cancellation support. Cancellation is addressed to the same
call and coroutine IDs. When cancellation wins, the caller stops waiting and a later result is not
admitted or resumed. Provider cooperation can be requested, but an external effect that already
occurred is not rolled back.

`plugin.toml` declares marketplace metadata, requirements, and descriptors only. It does not register
live features, discover Razor components, run Lua, fetch marketplace archives, manage workers, or
change lifecycle state.

## Author tools

Use the versioned [plugin author reference](plugin-authoring/v1.md), the generated
[Lua 5.4 language-server stub](../sdk/lua/5.4/v1/blokebot.lua), and the executable
[published examples](../examples/plugins/README.md). The offline `BlokeBot.PluginHarness` author
tool creates and validates ordinary local projects:

```console
blokebot-plugin init community.my-plugin ./my-plugin
blokebot-plugin generate ./my-plugin
blokebot-plugin validate ./my-plugin
blokebot-plugin test ./my-plugin
```

`init` rejects an invalid plugin ID, a linked destination, or a non-empty destination before writing.
It creates `plugin.toml`, an author-owned Lua entry module, starter `tests.toml`, `.luarc.json`, and
versioned generated files under `.blokebot/lua/5.4/v1`. Plugin code imports the public facade with
`local blokebot = require("blokebot")`; the worker's generic module/operation dispatcher is private.
`generate` validates `plugin.toml` for every supported target, then atomically replaces only the
generator-owned SDK, plugin-specific settings, automation and handler types, and executable
`handler-skeletons.lua`. The skeleton artifact contains no-op implementations for every declared
handler which authors can copy or adapt into declared modules. Regeneration replaces that artifact
and never modifies author-owned Lua.

Named calls return their typed success value directly. `pcall` receives a tagged host-failure table
when the host rejects a call. An uncaught failure or authoritative cancellation ends the invocation;
neither is represented by an ambiguous nil value.

`validate` checks every supported runtime identifier and accepts a normal manifest-only package.
Package-local `tests.toml` metadata is optional author-test input and is not part of normal package
validation. `test` requires that metadata, repeats package validation, and executes its scenarios
through the current runtime's worker without installing into or joining production inventory, or
contacting Twitch or third parties.

Exit codes are typed by `PluginHarnessExitCode`: success is `0`, usage is `2`, invalid source is
`3`, validation failure is `4`, unavailable worker is `5`, test failure is `6`, output I/O failure
is `7`, rejected project initialization or generation is `8`, and cancellation is `130`.
For `test`, a missing, malformed, or semantically invalid `tests.toml` reports
`TestMetadataMissing`, `TestMetadataMalformed`, or `TestMetadataInvalid` and exits `6`. An invalid
source reports `SourceInvalid` and exits `3`; a rejected package reports `PackageRejected` and exits
`4` before worker execution.

## Public overlay widgets

A `widgets` declaration belongs to one feature and names a Lua `renderEntryPoint`, a declared HTML
`documentAsset`, additional declared `assets`, typed configuration fields and public-safe defaults.
It is separate from an embedded management page. The host resolves the installation, feature, full
overlay/document and widget instance; input contains only `configuration`. The trusted server
`context.current()` provides this resolved identity and `widget.mode` (`live`, `preview-live` or
`preview-sample`), not a browser-controlled identity. Preview handlers must be non-consuming:
they must not admit, drain, advance, acknowledge or complete production work.

Trusted server handlers may use their own settings, storage and HTTP APIs to produce a public-safe
projection. The returned value must contain no secrets, private viewer data, page sessions,
management actions or host authority. These responsibilities do not turn Lua into a malicious-code
sandbox. The browser receives only the public projection and short-lived GET-only declared asset
URLs, fenced to the resolved host, frozen live/private-preview document, widget and render lifetime.
Relative HTML/CSS/JS asset loading is supported; no generic private fetch or management bridge is
forwarded. Widget browser documents run script-enabled in an opaque-origin frame; executable asset
documents retain that authority boundary when opened directly.

## Widget configuration portability

Widget configuration fields may declare `portability = "portable"`, `"destinationBinding"` or
`"withheld"`. Missing metadata means withheld on full-document export, not a rejection of local
configuration or an existing plugin. `portable` declares the entire top-level value, including
nested maps/arrays, safe to transfer; that declaration is the trusted plugin author's responsibility.
Destination and withheld values, undeclared fields and unknown plugin configuration never transfer.
Defaults, installation settings, protected secrets, storage and live output are not export fallbacks.

An imported widget retains source identity, layout and audio but requires explicit destination setup.
The destination's current declaration governs which supplied values remain portable. Optional omitted
fields also require setup confirmation; they do not auto-bind just because local validation succeeds.
Old manifests remain usable on a new host. Older hosts reject the new TOML keys; authors using them
must set an appropriate `compatibility.minimumBlokeBotVersion`. Manifest/worker protocol versions and
Lua widget invocation inputs are unchanged.
