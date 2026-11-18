using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>Streams the rows of a command on demand. Dispose enumeration after early exit. Errors can occur after rows have been delivered.</summary>
    public interface IAsyncQueryExecutableRowStream
    {
        /// <summary>Renders the command now and executes it on enumeration through the retained or default connector. Another enumeration reruns it.</summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Keep its connection available until enumeration is disposed.</param>
        /// <param name="cancellationToken">Cancels execution and result reading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration; requested cancellation throws.</returns>
        IAsyncEnumerable<T> ExecuteAsyncStream<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);

        /// <summary>Renders the command now and executes it on enumeration through the supplied connector. Another enumeration reruns it.</summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector used for this sequence.</param>
        /// <param name="transaction">An optional caller-owned transaction. Keep its connection available until enumeration is disposed.</param>
        /// <param name="cancellationToken">Cancels execution and result reading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration; requested cancellation throws.</returns>
        IAsyncEnumerable<T> ExecuteAsyncStream<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null);
    }
}
