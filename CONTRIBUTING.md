# Contributing to BlokeBot

Contributions should make one focused, reviewable change. Discuss changes that alter public
behavior, persisted data, security, compatibility, or project scope before investing in an
implementation.

Do not commit credentials, tokens, local configuration, databases, or generated build output.

## Development environment

The recommended environment is the repository's Nix development shell:

```console
nix develop
```

Without Nix, install the .NET SDK selected by `global.json` and Node.js with npm. Restore the
pinned CSharpier tool before formatting:

```console
dotnet tool restore
```

The [development guide](https://github.com/alsi-lawr/BlokeBot/wiki/Development) documents the
toolchain and frontend workflow in more detail.

## Build and test

Restore and build the complete solution:

```console
dotnet restore BlokeBot.slnx --disable-parallel
dotnet build BlokeBot.slnx --no-restore --disable-parallel -warnaserror
```

Run the active test set:

```console
dotnet test BlokeBot.slnx --no-restore --disable-parallel -v:minimal
```

BlokeBot uses TUnit on Microsoft Testing Platform. Use `--treenode-filter` for focused runs, not
the legacy VSTest `--filter` option.

Tests should protect observable behavior owned by BlokeBot. Prefer focused feature tests. Add a
characterization test only when existing behavior genuinely needs to be captured before it is
changed. Do not add reflection-based shape tests, tests of .NET or framework behavior, or tests
that merely restate plainly visible code.

## Formatting

CSharpier is the repository's C# formatter:

```console
dotnet csharpier format .
dotnet csharpier check .
```

Run `nix fmt` when changing Nix files. Build and analyzer verification remain separate from
formatting.

## Submitting a change

- Keep the change limited to its stated purpose and remove anything it supersedes.
- Update user documentation when public behavior changes.
- Include only tests that protect meaningful product behavior.
- State what you verified and identify any remaining risk in the pull request.

## Licence status

No licence is currently declared for this repository. Public visibility does not grant permission
to use, modify, or redistribute the project, and a contribution does not establish a project-wide
licence. Licence selection remains a maintainer decision.
