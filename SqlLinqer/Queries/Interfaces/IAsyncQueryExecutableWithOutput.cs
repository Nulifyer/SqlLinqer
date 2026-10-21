using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A <see cref="IQueryTyped"/> that can be executed and will return some output.
    /// </summary>
    public interface IAsyncQueryExecutableWithOutput
    {
        /// <summary>
        /// Asynchronously executes the query, capturing database errors and propagating requested cancellation.
        /// Uses the connector supplied at query creation, or <see cref="SqlLinqer.Default.Connector"/> if none was supplied.
        /// </summary>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result reading.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously executes the query, capturing database errors and propagating requested cancellation.
        /// </summary>
        /// <param name="connector">The connector used to render and execute this call, without changing the query's retained connector.</param>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result reading.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        Task<SqlResponse<DataTable>> ExecuteWithOutputAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default);
    }
}
