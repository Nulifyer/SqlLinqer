using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>Streams complete root objects on demand. Dispose enumeration after early exit. Errors can occur after rows have been delivered.</summary>
    /// <typeparam name="T">The streamed root model or projection.</typeparam>
    public interface IAsyncQueryExecutableStream<T>
    {
        /// <summary>Captures the query now and executes it on enumeration through the retained or default connector. Another enumeration reruns it.</summary>
        /// <param name="transaction">An optional caller-owned transaction. Keep its connection available until enumeration is disposed.</param>
        /// <param name="cancellationToken">Cancels execution and result reading.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration; requested cancellation throws.</returns>
        IAsyncEnumerable<T> ExecuteAsyncStream(DbTransaction transaction = null, CancellationToken cancellationToken = default);

        /// <summary>Captures the query now and executes it on enumeration through the supplied connector. Another enumeration reruns it.</summary>
        /// <param name="connector">The connector used for this sequence.</param>
        /// <param name="transaction">An optional caller-owned transaction. Keep its connection available until enumeration is disposed.</param>
        /// <param name="cancellationToken">Cancels execution and result reading.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration; requested cancellation throws.</returns>
        IAsyncEnumerable<T> ExecuteAsyncStream(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default);
    }
}
