using System.Threading;
using System.Threading.Tasks;
using System;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Where;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Joins;
using System.Runtime.CompilerServices;

namespace SqlLinqer.Queries.Typed
{
    public partial class SelectPrimaryKeyExistsQuery<T, TPk>
    {
        /// <summary>
        /// Asynchronously checks for the primary key without loading a row. Check State before interpreting a false result.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction with an open connection.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A valid true result for an existing key, valid false when absent, or an error response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<bool>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously checks for the primary key without loading a row. Check State before interpreting a false result.
        /// </summary>
        /// <param name="connector">The connector used for this execution only.</param>
        /// <param name="transaction">An optional caller-owned transaction with an open connection.</param>
        /// <param name="cancellationToken">Cancels provider I/O. Requested cancellation throws.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>A valid true result for an existing key, valid false when absent, or an error response.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<SqlResponse<bool>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var renderedQuery = new SqlLinqer.Components.Render.RenderedQuery();
            renderedQuery.Text = this.RenderRows(renderedQuery.Parameters, connector.DbFlavor);
            var response = await connector.ExecuteScalarAsync<int>(renderedQuery, transaction, cancellationToken: cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false);

            var bool_response = new SqlResponse<bool>();
            if (response.State == ResponseState.Error)
                bool_response.SetError(response.Error);
            else
                bool_response.SetResult(response.Result > 0);

            return bool_response;
        }
    }
}
