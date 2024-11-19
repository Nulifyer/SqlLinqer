using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A <see cref="IQueryTyped"/> that can be executed with tvp
    /// </summary>
    /// <typeparam name="T">The return type of the execution result</typeparam>
    public interface IQueryExecutableTvp<T>
    {
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// Using the default connector. <see cref="SqlLinqer.Default.Connector"/>
        /// </summary>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlResponse<T> ExecuteTvp(DbTransaction transaction = null);
        
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// </summary>
        /// <param name="connector">The connector used to execute the query.</param>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlResponse<T> ExecuteTvp(BaseConnector connector, DbTransaction transaction = null);
    }
}