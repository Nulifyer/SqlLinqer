using System.Data;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a write with a buffered modified-row projection.
    /// </summary>
    public interface IQueryExecutableWithOutput
    {
        /// <summary>
        /// Executes a write and buffers the selected modified columns in a DataTable that the caller must dispose. Check the response State before reading its Result.
        /// Uses the retained connector or the current default. MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The buffered modified rows and execution state. Dispose the Result table after a successful response.</returns>
        SqlResponse<DataTable> ExecuteWithOutput(DbTransaction transaction = null);

        /// <summary>
        /// Executes a write and buffers the selected modified columns in a DataTable that the caller must dispose. Check the response State before reading its Result.
        /// MySQL modified-row output is unsupported.
        /// </summary>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector or rebuild the query model mappings and schema.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <returns>The buffered modified rows and execution state. Dispose the Result table after a successful response.</returns>
        SqlResponse<DataTable> ExecuteWithOutput(BaseConnector connector, DbTransaction transaction = null);
    }
}
