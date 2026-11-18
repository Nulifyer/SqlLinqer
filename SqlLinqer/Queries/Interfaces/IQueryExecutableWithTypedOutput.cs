using System;
using System.Collections.Generic;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a write and maps its Output columns onto a row type instead of a DataTable.
    /// </summary>
    public interface IQueryExecutableWithTypedOutput
    {
        /// <summary>
        /// Executes the write and maps its Output columns onto TRow by column name, or reads a single output column into a scalar list.
        /// MySQL does not return modified rows.
        /// </summary>
        /// <typeparam name="TRow">A row class, or a scalar type for a single output column.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The returned rows and execution state.</returns>
        SqlResponse<List<TRow>> ExecuteWithOutput<TRow>(DbTransaction transaction = null, TimeSpan? commandTimeout = null);

        /// <summary>
        /// Executes the write on the supplied connector and maps its Output columns onto TRow by column name.
        /// </summary>
        /// <typeparam name="TRow">A row class, or a scalar type for a single output column.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The returned rows and execution state.</returns>
        SqlResponse<List<TRow>> ExecuteWithOutput<TRow>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null);
    }
}
