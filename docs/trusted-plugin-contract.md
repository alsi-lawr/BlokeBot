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
version and one mutable Git tag. A tag must not be a commit hash. Manifests, catalogue records, and
installation records do not have a commit-SHA identity field.

## Package content

The canonical package contains `blokebot.plugin.json`, declared `.lua` source modules, and declared
bounded browser or media assets. It does not contain native binaries, .NET assemblies, LuaRocks
artifacts, native dependencies, undeclared files, path escapes, case-colliding paths, or links.
Every file is subject to per-entry and total package limits.

## Host boundary

The host API exchanges closed typed values and outcomes. Calls identify their host module,
operation, coroutine, and installation, channel, automation, migration, or page context. A host call
can complete with a value, a typed failure, or cancellation. Asynchronous host work suspends only
the originating Lua coroutine and resumes it at most once. Cancellation is cooperative and is
addressed to the same call and coroutine IDs.

Manifests declare requirements and descriptors only. They do not register live features, discover
Razor components, run Lua, fetch marketplace archives, manage workers, or change lifecycle state.
