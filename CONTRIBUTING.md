# Contributing to BlokeBot

Keep changes focused and do not commit credentials, local configuration, databases, or generated output.

Enter the supported development environment with `nix develop`, or install the .NET SDK selected by `global.json` plus Node.js and npm.

```console
./scripts/verify-release --expected-migration-count 2
```

That single gate restores the solution and local tools, checks the Git diff and C# formatting, builds
with warnings as errors, runs the complete Microsoft Testing Platform suite, checks EF migration
topology/model drift, and proves that the captured minimum Hetzner schema upgrades to the same
integrity-clean schema as a fresh migration.

Exact-commit reviews can additionally pin the reviewed range and a compact test-method budget:

```console
./scripts/verify-release \
  --base-ref <accepted-parent> \
  --expected-head <reviewed-commit> \
  --expected-parent <exact-parent> \
  --max-new-test-methods <limit> \
  --expected-migration-count 2
```

Use `--treenode-filter` for focused TUnit/Microsoft Testing Platform runs. Format C# with `dotnet csharpier format .`.

For Nix changes:

```console
nix fmt
nix flake check --no-build --all-systems
```

See the technical [development guide](https://github.com/alsi-lawr/BlokeBot/wiki/Development), [installation guide](https://github.com/alsi-lawr/BlokeBot/wiki/Installation), and [server owner guide](https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide).
