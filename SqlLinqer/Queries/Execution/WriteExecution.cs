using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Execution
{
    // Single-command writes share key capture. Insert batching owns its aggregate count and transaction.
    internal static class WriteExecution
    {
        // Rejects a typed-output request that cannot succeed, before the write runs.
        internal static void ValidateTypedOutput<TRow>(SqlLinqer.Components.Outputs.OutputsCollection outputs)
        {
            if (outputs.Count == 0)
                throw new InvalidOperationException("ExecuteWithOutput<TRow> returns the columns chosen with Output. Call Output for each column to return.");
            if (outputs.Count != 1 && SqlLinqer.Components.Modeling.TabularRowType.GetCached(typeof(TRow)).IsScalar)
                throw new InvalidOperationException($"The row type {typeof(TRow).Name} holds one value, but the write returns {outputs.Count} output columns. "
                    + "Use a row class with a member for each column. A primary-key builder always returns its key as an output column.");
        }

        internal static SqlResponse<int> Execute(RenderedQuery query, BaseConnector connector, DbTransaction transaction, List<object> primaryKeys, TimeSpan? commandTimeout = null)
            => primaryKeys == null ? connector.ExecuteNonQuery(query, transaction, commandTimeout: commandTimeout)
                : CapturePrimaryKeys(connector.ExecuteReaderDataTable(query, transaction, commandTimeout: commandTimeout), primaryKeys);

        internal static async Task<SqlResponse<int>> ExecuteAsync(RenderedQuery query, BaseConnector connector, DbTransaction transaction,
            List<object> primaryKeys, CancellationToken cancellationToken, TimeSpan? commandTimeout = null)
            => primaryKeys == null ? await connector.ExecuteNonQueryAsync(query, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false)
                : CapturePrimaryKeys(await connector.ExecuteReaderDataTableAsync(query, transaction, cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false), primaryKeys);

        private static SqlResponse<int> CapturePrimaryKeys(SqlResponse<DataTable> response, List<object> primaryKeys)
        {
            if (response.State == ResponseState.Error) return new SqlResponse<int>(response.Error);
            using (var table = response.Result)
                foreach (DataRow row in table.Rows) primaryKeys.Add(row[0]);
            return new SqlResponse<int>(primaryKeys.Count);
        }
    }
}
