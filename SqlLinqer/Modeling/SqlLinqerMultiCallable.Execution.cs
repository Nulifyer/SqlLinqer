using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Modeling
{
    public abstract partial class SqlLinqerMultiCallable<TInput, TResults>
    {
        /// <summary>Executes using the retained or current default connector and returns output rows and standard metadata.</summary>
        /// <param name="input">The input model for this call. Null properties omit parameters so database defaults apply.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The output rows, execution state, RETURN code, and affected-row count.</returns>
        public SqlMultiCallableResponse<TResults> Execute(TInput input, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => CreateQuery(input).Execute(_connector ?? Default.Connector, transaction, commandTimeout: commandTimeout);

        /// <summary>Executes using an explicit connector for this call. Failure clears rows and metadata.</summary>
        /// <param name="input">The input model for this call.</param>
        /// <param name="connector">The SQL Server or Azure SQL connector used for this call.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The output rows and standard metadata. Unknown counts remain null.</returns>
        public SqlMultiCallableResponse<TResults> Execute(TInput input, BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => CreateQuery(input).Execute(connector, transaction, commandTimeout: commandTimeout);

        /// <summary>Asynchronously executes using the retained or current default connector. Requested cancellation throws.</summary>
        /// <param name="input">The input model for this call. Member values are captured before asynchronous work starts.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="cancellationToken">Cancels opening, execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The output rows and standard metadata.</returns>
        public Task<SqlMultiCallableResponse<TResults>> ExecuteAsync(TInput input, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => CreateQuery(input).ExecuteAsync(_connector ?? Default.Connector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously executes using an explicit connector for this call. Requested cancellation throws.</summary>
        /// <param name="input">The input model for this call. Member values are captured before asynchronous work starts.</param>
        /// <param name="connector">The SQL Server or Azure SQL connector used for this call.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="cancellationToken">Cancels opening, execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The output rows and standard metadata. Failure clears all output.</returns>
        public Task<SqlMultiCallableResponse<TResults>> ExecuteAsync(TInput input, BaseConnector connector, DbTransaction transaction = null,
            CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => CreateQuery(input).ExecuteAsync(connector, transaction, cancellationToken, commandTimeout: commandTimeout);
    }
}
