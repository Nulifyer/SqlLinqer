using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Modeling
{
    public abstract partial class SqlLinqerMultiCallable<TResults>
    {
        /// <summary>Executes without input parameters using the retained or current default connector.</summary>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout. Null uses its default; zero disables the timeout.</param>
        /// <returns>The output rows, execution state, RETURN code, and affected-row count.</returns>
        public SqlMultiCallableResponse<TResults> Execute(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => CreateQuery().Execute(_connector ?? Default.Connector, transaction, commandTimeout: commandTimeout);

        /// <summary>Executes without input parameters using an explicit connector. Failure clears rows and metadata.</summary>
        /// <param name="connector">The SQL Server or Azure SQL connector used for this call.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout. Null uses its default; zero disables the timeout.</param>
        /// <returns>The output rows and standard metadata. Unknown counts remain null.</returns>
        public SqlMultiCallableResponse<TResults> Execute(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => CreateQuery().Execute(connector, transaction, commandTimeout: commandTimeout);

        /// <summary>Asynchronously executes without inputs using the retained or current default connector. Requested cancellation throws.</summary>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="cancellationToken">Cancels opening, execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout. Null uses its default; zero disables the timeout.</param>
        /// <returns>The output rows and standard metadata.</returns>
        public Task<SqlMultiCallableResponse<TResults>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default,
            TimeSpan? commandTimeout = null)
            => CreateQuery().ExecuteAsync(_connector ?? Default.Connector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously executes without inputs using an explicit connector. Requested cancellation throws.</summary>
        /// <param name="connector">The SQL Server or Azure SQL connector used for this call.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="cancellationToken">Cancels opening, execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout. Null uses its default; zero disables the timeout.</param>
        /// <returns>The output rows and standard metadata. Failure clears all output.</returns>
        public Task<SqlMultiCallableResponse<TResults>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null,
            CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => CreateQuery().ExecuteAsync(connector, transaction, cancellationToken, commandTimeout: commandTimeout);
    }
}
