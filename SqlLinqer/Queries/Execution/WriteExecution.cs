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
        internal static SqlResponse<int> Execute(RenderedQuery query, BaseConnector connector, DbTransaction transaction, List<object> primaryKeys)
            => primaryKeys == null ? connector.ExecuteNonQuery(query, transaction)
                : CapturePrimaryKeys(connector.ExecuteReaderDataTable(query, transaction), primaryKeys);

        internal static async Task<SqlResponse<int>> ExecuteAsync(RenderedQuery query, BaseConnector connector, DbTransaction transaction,
            List<object> primaryKeys, CancellationToken cancellationToken)
            => primaryKeys == null ? await connector.ExecuteNonQueryAsync(query, transaction, cancellationToken).ConfigureAwait(false)
                : CapturePrimaryKeys(await connector.ExecuteReaderDataTableAsync(query, transaction, cancellationToken).ConfigureAwait(false), primaryKeys);

        private static SqlResponse<int> CapturePrimaryKeys(SqlResponse<DataTable> response, List<object> primaryKeys)
        {
            if (response.State == ResponseState.Error) return new SqlResponse<int>(response.Error);
            using (var table = response.Result)
                foreach (DataRow row in table.Rows) primaryKeys.Add(row[0]);
            return new SqlResponse<int>(primaryKeys.Count);
        }
    }
}
