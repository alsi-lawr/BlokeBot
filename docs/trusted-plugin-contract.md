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
version and one mutable Git tag. A tag must not be a commit hash. Manifests, derived marketplace
snapshots, and installation records do not have a commit-SHA identity field.

The curated repository layout is `plugins/<plugin-id>/plugin.toml`. The directory name must exactly
match the manifest ID. The manifest owns the author, search tags, optional presentation URLs,
release targets, version, mutable tag, compatibility, and runtime declarations. There is no global
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
tool accepts any local source and output directory:

```console
blokebot-plugin validate ./my-plugin
blokebot-plugin test ./my-plugin
blokebot-plugin generate-sdk ./author-kit
```

`validate` checks every supported runtime identifier and accepts a normal manifest-only package.
Package-local `tests.toml` metadata is optional author-test input and is not part of normal package
validation. `test` requires that metadata, repeats package validation, and executes its scenarios
through the current runtime's worker without installing into or joining production inventory, or
contacting Twitch or third parties. `generate-sdk` writes the canonical Lua stub and generated author
reference beneath the selected output directory.

Exit codes are typed by `PluginHarnessExitCode`: success is `0`, usage is `2`, invalid source is
`3`, validation failure is `4`, unavailable worker is `5`, test failure is `6`, output I/O failure
is `7`, and cancellation is `130`.
For `test`, a missing, malformed, or semantically invalid `tests.toml` reports
`TestMetadataMissing`, `TestMetadataMalformed`, or `TestMetadataInvalid` and exits `6`. An invalid
source reports `SourceInvalid` and exits `3`; a rejected package reports `PackageRejected` and exits
`4` before worker execution.
