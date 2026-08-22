namespace BlokeBot.Plugins.Contracts;

public enum PluginHostCallErrorCode
{
    EmptyCallId,
    EmptyCoroutineId,
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
        if (call.CallId == Guid.Empty)
        {
            errors.Add(new(PluginHostCallErrorCode.EmptyCallId, "$.callId"));
        }
        if (call.CoroutineId == Guid.Empty)
        {
            errors.Add(new(PluginHostCallErrorCode.EmptyCoroutineId, "$.coroutineId"));
        }
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

        return errors.Count == 0 ? new PluginHostCallValidationOutcome.Valid() : Invalid(errors);
    }

    private static PluginHostCallValidationOutcome.Invalid Invalid(
        List<PluginHostCallError> errors
    ) => new(errors.AsReadOnly());
}
