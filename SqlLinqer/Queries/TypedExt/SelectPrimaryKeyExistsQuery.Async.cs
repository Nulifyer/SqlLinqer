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
        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<bool>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Asynchronously executes the query. Requested cancellation propagates to the caller.</summary>
        public async Task<SqlResponse<bool>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            var renderedQuery = new SqlLinqer.Components.Render.RenderedQuery();
            renderedQuery.Text = this.Render(renderedQuery.Parameters, connector.DbFlavor, false);
            var response = await connector.ExecuteScalarAsync<int>(renderedQuery, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            var bool_response = new SqlResponse<bool>();
            if (response.State == ResponseState.Error)
                bool_response.SetError(response.Error);
            else
                bool_response.SetResult(response.Result > 0);

            return bool_response;
        }
    }
}
