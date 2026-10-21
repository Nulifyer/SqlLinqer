using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A <see cref="IQueryTyped"/> that can be executed with tvp
    /// </summary>
    /// <typeparam name="T">The return type of the execution result</typeparam>
    /// <typeparam name="TPk">The primary key type</typeparam>
    public interface IQueryExecutableTvpWithPrimaryKeys<T, TPk>
    {
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// Uses the connector supplied at query creation, or <see cref="SqlLinqer.Default.Connector"/> if none was supplied.
        /// </summary>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(DbTransaction transaction = null);
        
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// </summary>
        /// <param name="connector">The connector used to render and execute this call, without changing the query's retained connector.</param>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(BaseConnector connector, DbTransaction transaction = null);
    }
}