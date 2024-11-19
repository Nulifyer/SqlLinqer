using System.Data;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A <see cref="IQueryTyped"/> that can be executed and will return some output.
    /// </summary>
    public interface IQueryExecutableWithOutput
    {
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// Using the default connector. <see cref="SqlLinqer.Default.Connector"/>
        /// </summary>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlResponse<DataTable> ExecuteWithOutput(DbTransaction transaction = null);
        
        /// <summary>
        /// Executes the query. Returning the results and capturing any errors.
        /// </summary>
        /// <param name="connector">The connector used to execute the query.</param>
        /// <param name="transaction">An existing database transactiong with the connection property set to the open connection.</param>
        /// <returns>The retult of the query in a state based wrapper.</returns>
        SqlResponse<DataTable> ExecuteWithOutput(BaseConnector connector, DbTransaction transaction = null);
    }
}