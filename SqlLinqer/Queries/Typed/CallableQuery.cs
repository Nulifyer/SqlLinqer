using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Queries.Typed
{
    internal sealed partial class CallableQuery<TInput, TOutput> where TInput : class
    {
        private readonly CallableArguments<TInput> _arguments;
        private readonly TabularRowType _row;

        internal CallableQuery(SqlProcedure procedure, TInput input)
        {
            _arguments = new CallableArguments<TInput>(procedure, input);
            _row = TabularRowType.GetCached(typeof(TOutput));
        }

        internal RenderedQuery Render(DbFlavor flavor) => _arguments.Prepare(flavor, null).Query;
        internal RenderedQuery Render(BaseConnector connector) => Prepare(connector).Query;
        private PreparedCallable Prepare(BaseConnector connector) => _arguments.Prepare(connector);
    }
}
