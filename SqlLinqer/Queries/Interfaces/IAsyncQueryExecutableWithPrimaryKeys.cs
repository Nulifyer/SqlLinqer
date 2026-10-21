using System.Threading;
using System.Threading.Tasks;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A <see cref="IQueryTyped"/> that can be executed.
    /// </summary>
    /// <typeparam name="T">The return type of the execution result</typeparam>
    /// <typeparam name="TPk">The type of the primary key</typeparam>
    public interface IAsyncQueryExecutableWithPrimaryKeys<T, TPk>
    {
        /// <summary>
        /// Asynchronously executes the query, capturing database errors and propagating requested cancellation.
        /// Uses the connector supplied at query creation, or <see cref="SqlLinqer.Default.Connector"/> if none was supplied.
        /// </summary>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result reading.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously executes the query, capturing database errors and propagating requested cancellation.
        /// </summary>
        /// <param name="connector">The connector used to render and execute this call, without changing the query's retained connector.</param>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result reading.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default);
    }
}
