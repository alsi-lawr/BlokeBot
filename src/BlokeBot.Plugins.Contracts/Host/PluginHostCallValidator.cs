using System.Text;

namespace BlokeBot.Plugins.Contracts;

public enum PluginHostCallErrorCode
{
    WrongModule,
    UnknownOperation,
    ContextNotPermitted,
    ArgumentCountMismatch,
    ArgumentKindMismatch,
    InvalidArgumentValue,
    ArgumentPayloadTooLarge,
    InvalidResultValue,
    ResultKindMismatch,
    ResultPayloadTooLarge,
    InvalidOutcome,
    InvalidFailureCode,
    InvalidFailureMessage,
    FailureMessageTooLarge,
    InvalidCancellationReason,
    CallIdMismatch,
    CoroutineIdMismatch,
}

public sealed record PluginHostCallError(PluginHostCallErrorCode Code, string Location);

public abstract record PluginHostCallValidationOutcome
{
    private PluginHostCallValidationOutcome() { }

    public sealed record Valid : PluginHostCallValidationOutcome;

    public sealed record Invalid(IReadOnlyList<PluginHostCallError> Errors)
        : PluginHostCallValidationOutcome;
}

public static class PluginHostCallValidator
{
    public static PluginHostCallValidationOutcome ValidateCall(
        PluginHostCall call,
        PluginHostModuleDescriptor module
    )
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(module);
        var errors = new List<PluginHostCallError>();
        var operation = ResolveOperation(call, module, errors);
        if (operation is null)
        {
            return Invalid(errors);
        }

        if (!operation.PermittedContexts.Contains(call.Context.Kind))
        {
            errors.Add(new(PluginHostCallErrorCode.ContextNotPermitted, "$.context"));
        }
        if (operation.ArgumentKinds.Length != call.Arguments.Length)
        {
            errors.Add(new(PluginHostCallErrorCode.ArgumentCountMismatch, "$.arguments"));
            return Invalid(errors);
        }

        long argumentBytes = 0;
        for (var index = 0; index < call.Arguments.Length; index++)
        {
            var argument = call.Arguments[index];
            if (argument.Kind != operation.ArgumentKinds[index])
            {
                errors.Add(
                    new(PluginHostCallErrorCode.ArgumentKindMismatch, $"$.arguments[{index}]")
                );
            }

            if (PluginValueValidator.Validate(argument) is PluginValueValidationOutcome.Valid valid)
            {
                argumentBytes += valid.PayloadBytes;
            }
            else
            {
                errors.Add(
                    new(PluginHostCallErrorCode.InvalidArgumentValue, $"$.arguments[{index}]")
                );
            }
        }

        if (argumentBytes > operation.MaximumArgumentBytes)
        {
            errors.Add(new(PluginHostCallErrorCode.ArgumentPayloadTooLarge, "$.arguments"));
        }
        return errors.Count == 0 ? new PluginHostCallValidationOutcome.Valid() : Invalid(errors);
    }

    public static PluginHostCallValidationOutcome ValidateReturnedValue(
        PluginValue value,
        PluginHostOperationDescriptor operation
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(operation);
        var errors = new List<PluginHostCallError>();
        ValidateReturnedValue(value, operation, errors);
        return Complete(errors);
    }

    public static PluginHostCallValidationOutcome ValidateOutcome(
        PluginHostCallOutcome outcome,
        PluginHostOperationDescriptor operation
    )
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(operation);
        var errors = new List<PluginHostCallError>();
        ValidateOutcome(outcome, operation, errors);
        return Complete(errors);
    }

    public static PluginHostCallValidationOutcome ValidateCompletion(
        PluginHostCall call,
        PluginHostCallCompletion completion,
        PluginHostModuleDescriptor module
    )
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(module);
        var errors = new List<PluginHostCallError>();
        ValidateBinding(call, completion.CallId, completion.CoroutineId, errors);
        if (ResolveOperation(call, module, errors) is { } operation)
        {
            ValidateOutcome(completion.Outcome, operation, errors);
        }
        return Complete(errors);
    }

    public static PluginHostCallValidationOutcome ValidateCancellation(
        PluginHostCall call,
        PluginHostCallCancellation cancellation
    )
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(cancellation);
        var errors = new List<PluginHostCallError>();
        ValidateBinding(call, cancellation.CallId, cancellation.CoroutineId, errors);
        ValidateCancellationReason(cancellation.Reason, "$.reason", errors);
        return Complete(errors);
    }

    private static void ValidateOutcome(
        PluginHostCallOutcome? outcome,
        PluginHostOperationDescriptor operation,
        List<PluginHostCallError> errors
    )
    {
        switch (outcome)
        {
            case PluginHostCallOutcome.Returned returned:
                ValidateReturnedValue(returned.Value, operation, errors);
                break;
            case PluginHostCallOutcome.Failed failed:
                ValidateFailure(failed.Failure, errors);
                break;
            case PluginHostCallOutcome.Cancelled cancelled:
                ValidateCancellationReason(cancelled.Reason, "$.cancellation.reason", errors);
                break;
            case null:
                errors.Add(new(PluginHostCallErrorCode.InvalidOutcome, "$.outcome"));
                break;
        }
    }

    private static void ValidateReturnedValue(
        PluginValue? value,
        PluginHostOperationDescriptor operation,
        List<PluginHostCallError> errors
    )
    {
        if (value is null)
        {
            errors.Add(new(PluginHostCallErrorCode.InvalidResultValue, "$.result"));
            return;
        }

        if (value.Kind != operation.ResultKind)
        {
            errors.Add(new(PluginHostCallErrorCode.ResultKindMismatch, "$.result"));
        }

        if (PluginValueValidator.Validate(value) is not PluginValueValidationOutcome.Valid valid)
        {
            errors.Add(new(PluginHostCallErrorCode.InvalidResultValue, "$.result"));
        }
        else if (valid.PayloadBytes > operation.MaximumResultBytes)
        {
            errors.Add(new(PluginHostCallErrorCode.ResultPayloadTooLarge, "$.result"));
        }
    }

    private static void ValidateFailure(
        PluginHostFailure? failure,
        List<PluginHostCallError> errors
    )
    {
        if (failure is not null && !Enum.IsDefined(failure.Code))
        {
            errors.Add(new(PluginHostCallErrorCode.InvalidFailureCode, "$.failure.code"));
        }

        if (
            failure is null
            || string.IsNullOrWhiteSpace(failure.SafeMessage)
            || failure.SafeMessage.Any(char.IsControl)
        )
        {
            errors.Add(new(PluginHostCallErrorCode.InvalidFailureMessage, "$.failure.safeMessage"));
            return;
        }

        if (
            failure.SafeMessage.Length
                > PluginContractLimits.MaximumHostFailureSafeMessageCharacters
            || Encoding.UTF8.GetByteCount(failure.SafeMessage)
                > PluginContractLimits.MaximumHostFailureSafeMessageBytes
        )
        {
            errors.Add(
                new(PluginHostCallErrorCode.FailureMessageTooLarge, "$.failure.safeMessage")
            );
        }
    }

    private static void ValidateBinding(
        PluginHostCall call,
        PluginHostCallId callId,
        PluginCoroutineId coroutineId,
        List<PluginHostCallError> errors
    )
    {
        if (call.CallId != callId)
        {
            errors.Add(new(PluginHostCallErrorCode.CallIdMismatch, "$.callId"));
        }

        if (call.CoroutineId != coroutineId)
        {
            errors.Add(new(PluginHostCallErrorCode.CoroutineIdMismatch, "$.coroutineId"));
        }
    }

    private static void ValidateCancellationReason(
        PluginCancellationReason reason,
        string location,
        List<PluginHostCallError> errors
    )
    {
        if (!Enum.IsDefined(reason))
        {
            errors.Add(new(PluginHostCallErrorCode.InvalidCancellationReason, location));
        }
    }

    private static PluginHostOperationDescriptor? ResolveOperation(
        PluginHostCall call,
        PluginHostModuleDescriptor module,
        List<PluginHostCallError> errors
    )
    {
        if (call.Module != module.Id)
        {
            errors.Add(new(PluginHostCallErrorCode.WrongModule, "$.module"));
        }

        var operation = module.Operations.FirstOrDefault(candidate =>
            candidate.Id == call.Operation
        );
        if (operation is null)
        {
            errors.Add(new(PluginHostCallErrorCode.UnknownOperation, "$.operation"));
        }

        return operation;
    }

    private static PluginHostCallValidationOutcome Complete(List<PluginHostCallError> errors) =>
        errors.Count == 0
            ? new PluginHostCallValidationOutcome.Valid()
            : new PluginHostCallValidationOutcome.Invalid(errors.AsReadOnly());

    private static PluginHostCallValidationOutcome.Invalid Invalid(
        List<PluginHostCallError> errors
    ) => new(errors.AsReadOnly());
}
