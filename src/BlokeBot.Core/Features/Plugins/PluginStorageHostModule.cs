using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginStorageHostModule(PluginPrivateDataStore store) : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Storage;

    public ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PluginHostCallOutcome>(Unavailable());

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var sql = ((PluginValue.String)call.Arguments[0]).Value;
        var parameters = (PluginValue.Map)call.Arguments[1];
        var outcome =
            call.Operation == PluginStandardHostModules.Storage.Operations[0].Id
                ? await store.ExecuteAsync(identity, sql, parameters, cancellationToken)
                : await store.QueryAsync(identity, sql, parameters, cancellationToken);
        return outcome switch
        {
            PluginSqliteOutcome.Changed changed => new PluginHostCallOutcome.Returned(
                new PluginValue.Number(changed.Count)
            ),
            PluginSqliteOutcome.Rows rows => new PluginHostCallOutcome.Returned(
                new PluginValue.Array(rows.Values.Cast<PluginValue>().ToImmutableArray())
            ),
            PluginSqliteOutcome.Rejected rejected => new PluginHostCallOutcome.Failed(
                new(
                    rejected.Code
                        is PluginSqliteRejectionCode.InvalidParameters
                            or PluginSqliteRejectionCode.InvalidStatement
                        ? PluginHostFailureCode.InvalidArguments
                        : PluginHostFailureCode.ProviderRejected,
                    "Plugin SQLite operation was rejected."
                )
            ),
            _ => Unavailable(),
        };
    }

    private static PluginHostCallOutcome.Failed Unavailable() =>
        new(new(PluginHostFailureCode.Unavailable, "Plugin SQLite is unavailable."));
}
