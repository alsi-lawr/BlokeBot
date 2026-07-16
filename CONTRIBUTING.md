# Contributing to BlokeBot

Keep changes focused and do not commit credentials, local configuration, databases, or generated output.

Enter the supported development environment with `nix develop`, or install the .NET SDK selected by `global.json` plus Node.js and npm.

```console
dotnet restore BlokeBot.slnx
dotnet tool restore
dotnet csharpier check .
dotnet build BlokeBot.slnx --no-restore -warnaserror
dotnet test BlokeBot.slnx --no-build
```

Use `--treenode-filter` for focused TUnit/Microsoft Testing Platform runs. Format C# with `dotnet csharpier format .`.

For Nix changes:

```console
nix fmt
nix flake check --no-build --all-systems
```

See the technical [development guide](https://github.com/alsi-lawr/BlokeBot/wiki/Development), [installation guide](https://github.com/alsi-lawr/BlokeBot/wiki/Installation), and [server owner guide](https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide).
