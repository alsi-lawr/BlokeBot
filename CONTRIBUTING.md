# Contributing to BlokeBot

Keep changes focused and do not commit credentials, local configuration, databases, or generated output.

## Tickets and pull requests

Deliver each tracked implementation ticket through its own pull request and merge. Keep unrelated
tickets out of that pull request, and include `Closes #<issue-number>` in its description so the
merge closes the ticket. For an issue in another repository, use
`Closes <owner>/<repository>#<issue-number>`.

Enter the supported development environment with `nix develop`, or install the .NET SDK selected by `global.json` plus Node.js and npm.

```console
dotnet tool restore
dotnet csharpier check .
dotnet test BlokeBot.slnx --configuration Release --property:TreatWarningsAsErrors=true -- --no-ansi --no-progress --output Normal
```

These are the same direct formatting and test commands used by pull-request CI. The test command
restores and builds the solution before running the complete Microsoft Testing Platform suite.

Use `--treenode-filter` for focused TUnit/Microsoft Testing Platform runs. Format C# with `dotnet csharpier format .`.

EF Core migrations are generated artifacts. Verify that the model has no pending migration, but do
not add tests for generated migrations, database-provider constraints, or transaction semantics.
Tests should exercise BlokeBot behaviour rather than reproduce EF Core or the configured provider.

For Nix changes:

```console
nix fmt
nix flake check --no-build --all-systems
```

See the technical [development guide](https://github.com/alsi-lawr/BlokeBot/wiki/Development), [installation guide](https://github.com/alsi-lawr/BlokeBot/wiki/Installation), and [server owner guide](https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide).
