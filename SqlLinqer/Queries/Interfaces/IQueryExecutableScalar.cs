using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Executes a command and reads a single value from its result.
    /// </summary>
    public interface IQueryExecutableScalar
    {
        /// <summary>
        /// Reads the first value of the first row and converts it to T. Without a supplied transaction, the command runs in an owned transaction unless the query declines one.
        /// Uses the retained connector or the current default. Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="T">The scalar result type. SQL NULL is allowed only when the type can hold null.</typeparam>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The converted scalar and execution state.</returns>
        SqlResponse<T> ExecuteScalar<T>(DbTransaction transaction = null, TimeSpan? commandTimeout = null);
        /// <summary>
        /// Reads the first value of the first row and converts it to T. Without a supplied transaction, the command runs in an owned transaction unless the query declines one.
        /// Validation can throw before database execution.
        /// </summary>
        /// <typeparam name="T">The scalar result type. SQL NULL is allowed only when the type can hold null.</typeparam>
        /// <param name="connector">The connector used for this call. It does not replace the retained connector.</param>
        /// <param name="transaction">An optional transaction with an open connection. The caller owns commit, rollback, and disposal, including after errors or cancellation.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The converted scalar and execution state.</returns>
        SqlResponse<T> ExecuteScalar<T>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null);
    }
}
