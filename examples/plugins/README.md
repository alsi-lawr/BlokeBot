# Published plugin examples

These packages are executable author examples and failure fixtures for the Lua 5.4 v1 contract. Each package uses `plugin.toml`; optional `tests.toml` metadata drives only the local author harness. `BlokeBot.Plugins.Contracts.Tests` validates every package for every supported RID and runs every scenario through the published-for-tests worker. Host calls use deterministic local adapters; the matrix does not install a plugin, mutate a production inventory, contact Twitch, or call a third party.
