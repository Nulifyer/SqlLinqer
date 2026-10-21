using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.TypedExt
{
    public partial class InsertWithPrimaryKeyQuery<T, TPk>
    {
        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new SqlPrimaryKeysResponse<int, TPk>();
            var primaryKeys = new List<object>();

            var baseRes = await base.ExecuteAndCapturePrimaryKeysAsync(connector, transaction, primaryKeys, cancellationToken: cancellationToken).ConfigureAwait(false);

            switch (baseRes.State)
            {
                case ResponseState.Valid:
                    response.SetResult(baseRes.Result, primaryKeys.Select(x => connector.DbFlavor == DbFlavor.Sqlite
                        ? Components.Render.SqliteValue.ConvertTo<TPk>(x) : connector.DbFlavor == DbFlavor.DuckDb
                        ? DbValueConversion.ConvertTo<TPk>(x) : (TPk)x).ToList());
                    break;
                case ResponseState.Error:
                    response.SetError(baseRes.Error);
                    break;
                default:
                    throw new System.NotSupportedException($"The {nameof(ResponseState)} {baseRes.State} is not supported");
            }

            return response;
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteTvpAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExecuteTvpAsync(ExecutionConnector, transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public new async Task<SqlPrimaryKeysResponse<int, TPk>> ExecuteTvpAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new SqlPrimaryKeysResponse<int, TPk>();
            var primaryKeys = new List<object>();

            var baseRes = await base.ExecuteTvpAndCapturePrimaryKeysAsync(connector, transaction, primaryKeys, cancellationToken: cancellationToken).ConfigureAwait(false);

            switch (baseRes.State)
            {
                case ResponseState.Valid:
                    response.SetResult(baseRes.Result, primaryKeys.Select(x => connector.DbFlavor == DbFlavor.Sqlite
                        ? Components.Render.SqliteValue.ConvertTo<TPk>(x) : connector.DbFlavor == DbFlavor.DuckDb
                        ? DbValueConversion.ConvertTo<TPk>(x) : (TPk)x).ToList());
                    break;
                case ResponseState.Error:
                    response.SetError(baseRes.Error);
                    break;
                default:
                    throw new System.NotSupportedException($"The {nameof(ResponseState)} {baseRes.State} is not supported");
            }

            return response;
        }
    }
}
