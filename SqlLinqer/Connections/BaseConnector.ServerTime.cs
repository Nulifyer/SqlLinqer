using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        /// <summary>
        /// Reads the database server's current time as a UTC instant. The read does not open an owned transaction.
        /// Inside a PostgreSQL transaction the value is the transaction's start time, as CURRENT_TIMESTAMP is.
        /// </summary>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The server time with a zero offset, and the execution state.</returns>
        public SqlResponse<DateTimeOffset> GetServerTime(DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
        {
            var query = ServerTimeQuery();
            return DbFlavor == DbFlavor.DuckDb
                ? ToServerTime(ExecuteScalar<long>(query, existingTransaction, commandTimeout))
                : ToServerTime(ExecuteScalar<DateTime>(query, existingTransaction, commandTimeout));
        }

        /// <summary>
        /// Asynchronously reads the database server's current time as a UTC instant. Requested cancellation throws.
        /// </summary>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection.</param>
        /// <param name="cancellationToken">Cancels connection opening and command execution.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The server time with a zero offset, and the execution state.</returns>
        public async Task<SqlResponse<DateTimeOffset>> GetServerTimeAsync(DbTransaction existingTransaction = null,
            CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            var query = ServerTimeQuery();
            return DbFlavor == DbFlavor.DuckDb
                ? ToServerTime(await ExecuteScalarAsync<long>(query, existingTransaction, cancellationToken, commandTimeout).ConfigureAwait(false))
                : ToServerTime(await ExecuteScalarAsync<DateTime>(query, existingTransaction, cancellationToken, commandTimeout).ConfigureAwait(false));
        }

        private RenderedQuery ServerTimeQuery()
            => new RenderedQuery(ServerTimeOperation.ReadUtc(DbFlavor)) { CommandRole = "ServerTime", IsReadOnlySelect = true };

        // Every provider query returns UTC wall-clock time, whatever kind the provider reports.
        private static SqlResponse<DateTimeOffset> ToServerTime(SqlResponse<DateTime> response)
            => response.State != ResponseState.Valid ? new SqlResponse<DateTimeOffset>(response.Error)
                : new SqlResponse<DateTimeOffset>(new DateTimeOffset(DateTime.SpecifyKind(
                    response.Result.Kind == DateTimeKind.Local ? response.Result.ToUniversalTime() : response.Result, DateTimeKind.Utc)));

        // DuckDB reports microseconds since the Unix epoch.
        private static SqlResponse<DateTimeOffset> ToServerTime(SqlResponse<long> response)
            => response.State != ResponseState.Valid ? new SqlResponse<DateTimeOffset>(response.Error)
                : new SqlResponse<DateTimeOffset>(new DateTimeOffset(621355968000000000L + response.Result * 10, TimeSpan.Zero));
    }
}
