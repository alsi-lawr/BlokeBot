using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGuessingControlsTests
{
    private sealed class Measurements : DbCommandInterceptor, IMaterializationInterceptor
    {
        internal List<string> Commands { get; } = [];
        internal List<int> ReaderOperations { get; } = [];
        internal Dictionary<Type, int> Entities { get; } = [];

        internal void Reset()
        {
            Commands.Clear();
            ReaderOperations.Clear();
            Entities.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData data,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            Commands.Add(
                command.CommandText
                    + "\n"
                    + string.Join(
                        ", ",
                        command
                            .Parameters.Cast<DbParameter>()
                            .Select(x => $"{x.ParameterName}={x.Value}")
                    )
            );
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult DataReaderDisposing(
            DbCommand command,
            DataReaderDisposingEventData data,
            InterceptionResult result
        )
        {
            ReaderOperations.Add(data.ReadCount);
            return result;
        }

        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            Entities[entity.GetType()] = Entities.GetValueOrDefault(entity.GetType()) + 1;
            return entity;
        }
    }
}
