# Contributing

## Formatting

CSharpier is the repository formatter for C# and supported XML files. Restore the pinned local
tool before using it:

```console
dotnet tool restore
```

Format the repository:

```console
dotnet csharpier format .
```

Check formatting without changing files:

```console
dotnet csharpier check .
```

The formatting check is required before committing and exits with a failure when files differ.
Build and analyzer verification remain separate from formatting.
