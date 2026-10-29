using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Queries.Typed
{
    internal sealed class MultiCallableQuery<TInput, TResults> where TInput : class
    {
        private readonly CallableArguments<TInput> _arguments;
        private readonly MultiResultShape<TResults> _shape;

        internal MultiCallableQuery(SqlProcedure procedure, TInput input)
        {
            _arguments = new CallableArguments<TInput>(procedure, input);
            _shape = new MultiResultShape<TResults>();
        }

        internal RenderedQuery Render(DbFlavor flavor) => _arguments.Prepare(flavor, null, false).Query;
        internal RenderedQuery Render(BaseConnector connector) => _arguments.Prepare(connector, false).Query;

        internal SqlMultiCallableResponse<TResults> Execute(BaseConnector connector, DbTransaction transaction, TimeSpan? commandTimeout = null)
        {
            using (var plan = _arguments.Prepare(connector, false))
                return connector.ExecuteMultiCallable(plan, _shape, transaction, commandTimeout);
        }

        internal async Task<SqlMultiCallableResponse<TResults>> ExecuteAsync(BaseConnector connector, DbTransaction transaction,
            CancellationToken cancellationToken, TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var plan = _arguments.Prepare(connector, false))
            {
                plan.CaptureInputs();
                return await connector.ExecuteMultiCallableAsync(plan, _shape, transaction, cancellationToken, commandTimeout).ConfigureAwait(false);
            }
        }
    }
}
