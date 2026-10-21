using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A <see cref="IQueryTyped"/> that can be executed.
    /// </summary>
    /// <typeparam name="T">The return type of the execution result</typeparam>
    public interface IQueryExecutable<T>
    {
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// Uses the connector supplied at query creation, or <see cref="SqlLinqer.Default.Connector"/> if none was supplied.
        /// </summary>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlResponse<T> Execute(DbTransaction transaction = null);
        
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// </summary>
        /// <param name="connector">The connector used to render and execute this call, without changing the query's retained connector.</param>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlResponse<T> Execute(BaseConnector connector, DbTransaction transaction = null);
    }
}