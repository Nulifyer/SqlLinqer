using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        private static DbDataReader ReadCommand(DbCommand command, ExecutionObservation observation, CommandBehavior behavior = CommandBehavior.Default)
        {
            if (observation == null) return command.ExecuteReader(behavior);
            long started = Stopwatch.GetTimestamp();
            try { return command.ExecuteReader(behavior); }
            finally { observation.CommandFinished(started); }
        }
        private static Task<DbDataReader> ReadCommandAsync(DbCommand command, ExecutionObservation observation,
            CancellationToken token, CommandBehavior behavior = CommandBehavior.Default)
            => observation == null ? command.ExecuteReaderAsync(behavior, token) : ReadTimedAsync(command, observation, token, behavior);
        private static async Task<DbDataReader> ReadTimedAsync(DbCommand command, ExecutionObservation observation, CancellationToken token, CommandBehavior behavior)
        {
            long started = Stopwatch.GetTimestamp();
            try { return await command.ExecuteReaderAsync(behavior, token).ConfigureAwait(false); }
            finally { observation.CommandFinished(started); }
        }
        private static object ScalarCommand(DbCommand command, ExecutionObservation observation)
        {
            if (observation == null) return command.ExecuteScalar();
            long started = Stopwatch.GetTimestamp();
            try { return command.ExecuteScalar(); }
            finally { observation.CommandFinished(started); }
        }
        private static Task<object> ScalarCommandAsync(DbCommand command, ExecutionObservation observation, CancellationToken token)
            => observation == null ? command.ExecuteScalarAsync(token) : ScalarTimedAsync(command, observation, token);
        private static async Task<object> ScalarTimedAsync(DbCommand command, ExecutionObservation observation, CancellationToken token)
        {
            long started = Stopwatch.GetTimestamp();
            try { return await command.ExecuteScalarAsync(token).ConfigureAwait(false); }
            finally { observation.CommandFinished(started); }
        }
        private static int WriteCommand(DbCommand command, ExecutionObservation observation)
        {
            if (observation == null) return command.ExecuteNonQuery();
            long started = Stopwatch.GetTimestamp();
            try { return command.ExecuteNonQuery(); }
            finally { observation.CommandFinished(started); }
        }
        private static Task<int> WriteCommandAsync(DbCommand command, ExecutionObservation observation, CancellationToken token)
            => observation == null ? command.ExecuteNonQueryAsync(token) : WriteTimedAsync(command, observation, token);
        private static async Task<int> WriteTimedAsync(DbCommand command, ExecutionObservation observation, CancellationToken token)
        {
            long started = Stopwatch.GetTimestamp();
            try { return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
            finally { observation.CommandFinished(started); }
        }
    }
}
