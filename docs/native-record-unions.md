# Native Record Unions

Use a native record union for a finite domain state or outcome with more than two meaningful cases, especially when cases carry different payloads. Use `Result`, `Option`, `Validation`, or `IO` only for their narrower functional contracts.

## Placement and naming

- Keep the union beside its owning domain in the same project, namespace, and feature folder.
- Name the base and cases in domain language. Do not place feature cases in `BlokeBot.Functional` or a shared union catalogue.
- Keep payloads on the cases that can validly contain them, with invariants enforced by those case constructors.
- Do not add a union package, source generator, Boolean discriminator, or enum-plus-null payload bag.

## Required shape

At the repository's C# 14 language level, use all of these parts together:

1. A public abstract record base with a `private protected` constructor.
2. A `private protected abstract` seal member implemented by every case. This blocks an unsupported concrete case in another assembly, including derivation through a record's synthesized protected copy constructor.
3. Sealed record cases, normally nested under the base, with get-only case-local payloads.
4. An abstract `Match` method with one typed handler per case. Each case dispatches only to its own handler.
5. A contract test that compares every direct descendant with the `Match` handlers and verifies the seal and sealed cases.

C# 14 does not consider a type-pattern switch over an abstract record hierarchy exhaustive. Handle the union through `Match` instead. Do not add a wildcard, default handler, or fallback exception: those paths let a new case compile without updating consumers. When adding a case, extend `Match`; the resulting compiler errors identify every case implementation and call site that must become exhaustive.

The compiling convention example is [`NativeUnionExample.cs`](../tests/BlokeBot.Functional.Tests/NativeUnionExample.cs), and [`NativeUnionTests.cs`](../tests/BlokeBot.Functional.Tests/NativeUnionTests.cs) verifies case behavior, invariants, value semantics, closure, and handler coverage.
