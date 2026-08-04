using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public interface ICustomCommandAutomationRuntime
{
    Task<CustomCommandAutomationDispatchOutcome> DispatchAsync(
        CustomCommandAutomationDispatchRequest request,
        CancellationToken cancellationToken
    );

    Task<IReadOnlySet<int>> AvailableCommandIdsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    );
}

public sealed record CustomCommandAutomationDispatchRequest(
    AutomationTrigger Trigger,
    int CommandId,
    CustomCommandCooldownScope CooldownScope,
    string ViewerLogin,
    TimeSpan Cooldown,
    CustomCommandInvocationClaimRequest? InvocationClaim
);

public abstract record CustomCommandAutomationDispatchOutcome
{
    private CustomCommandAutomationDispatchOutcome() { }

    public sealed record Dispatched(AutomationDispatchOutcome Dispatch)
        : CustomCommandAutomationDispatchOutcome;

    public sealed record Cooldown : CustomCommandAutomationDispatchOutcome;

    public sealed record AlreadyUsed : CustomCommandAutomationDispatchOutcome;
}

internal abstract record CustomCommandAutomationAdmissionOutcome
{
    private CustomCommandAutomationAdmissionOutcome() { }

    internal sealed record Dispatched(AutomationDispatchOutcome Dispatch)
        : CustomCommandAutomationAdmissionOutcome;

    internal sealed record AlreadyUsed : CustomCommandAutomationAdmissionOutcome;
}

internal sealed class CustomCommandAutomationRuntime(
    AutomationRuntimeService runtime,
    CustomCommandCooldownStore cooldowns,
    CustomCommandInvocationClaimStore claims
) : ICustomCommandAutomationRuntime
{
    public async Task<CustomCommandAutomationDispatchOutcome> DispatchAsync(
        CustomCommandAutomationDispatchRequest request,
        CancellationToken cancellationToken
    )
    {
        using var reservation = await cooldowns.ReserveAsync(
            request.CommandId,
            request.CooldownScope,
            request.ViewerLogin,
            request.Cooldown,
            cancellationToken
        );
        if (reservation is null)
        {
            return new CustomCommandAutomationDispatchOutcome.Cooldown();
        }

        var dispatch = await runtime.DispatchCustomCommandAsync(
            request.Trigger,
            request.InvocationClaim is null
                ? null
                : (db, ct) => claims.TryClaimAsync(db, request.InvocationClaim, ct),
            reservation.Commit,
            cancellationToken
        );
        return dispatch switch
        {
            CustomCommandAutomationAdmissionOutcome.Dispatched dispatched =>
                new CustomCommandAutomationDispatchOutcome.Dispatched(dispatched.Dispatch),
            CustomCommandAutomationAdmissionOutcome.AlreadyUsed =>
                new CustomCommandAutomationDispatchOutcome.AlreadyUsed(),
            _ => throw new InvalidOperationException("Unknown custom-command automation outcome."),
        };
    }

    public Task<IReadOnlySet<int>> AvailableCommandIdsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    ) => runtime.AvailableCustomCommandIdsAsync(hostId, cancellationToken);
}

internal sealed class UnavailableCustomCommandAutomationRuntime : ICustomCommandAutomationRuntime
{
    public Task<CustomCommandAutomationDispatchOutcome> DispatchAsync(
        CustomCommandAutomationDispatchRequest request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<CustomCommandAutomationDispatchOutcome>(
            new CustomCommandAutomationDispatchOutcome.Dispatched(
                new(AutomationDispatchStatus.FeatureDisabled, [])
            )
        );

    public Task<IReadOnlySet<int>> AvailableCommandIdsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    ) => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
}
