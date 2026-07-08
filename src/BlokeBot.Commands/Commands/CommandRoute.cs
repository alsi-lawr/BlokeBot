namespace BlokeBot.Commands;

/// <summary>
/// A resolved command route with a feature-owned command kind and host-specific state.
/// </summary>
public sealed record CommandRoute<TKind, TState>(TKind Kind, TState State)
    where TKind : notnull;
