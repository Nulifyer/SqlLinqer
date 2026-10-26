using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines a SQL Server or Azure SQL procedure through SqlProcedure, with an input model and one output row model.
    /// Standard total-count outputs are detected automatically; callers do not configure response parameter names.
    /// Each execution reads the supplied input values. Null properties omit parameters, while empty strings and other values are supplied unchanged.
    /// Use SqlArgument only when explicit omission or SQL NULL is needed.
    /// </summary>
    /// <typeparam name="TInput">The input model with ordinary properties and optional SqlParameter metadata.</typeparam>
    /// <typeparam name="TOutput">The returned row model. Execute returns rows and standard metadata in SqlCallableResponse.</typeparam>
    public abstract partial class SqlLinqerCallable<TInput, TOutput> where TInput : class
    {
        private readonly SqlProcedure _procedure;
        private readonly BaseConnector _connector;

        /// <summary>Reads the SqlProcedure mapping from the derived class without executing SQL.</summary>
        /// <param name="connector">An optional retained connector. Procedure name and schema come from SqlProcedure.</param>
        protected SqlLinqerCallable(BaseConnector connector = null)
        {
            _procedure = SqlProcedureMapping.Get(GetType());
            _connector = connector;
        }

        /// <summary>Renders SQL and parameter bindings for the supplied input without opening a connection.</summary>
        /// <param name="input">The input model to render.</param>
        /// <param name="connector">The rendering connector, or null to use the retained or current default connector.</param>
        /// <returns>The parameterized procedure call.</returns>
        public RenderedQuery Render(TInput input, BaseConnector connector = null)
            => CreateQuery(input).Render(connector ?? _connector ?? Default.Connector);

        /// <summary>Renders SQL and parameter bindings for the selected dialect without opening a connection.</summary>
        /// <param name="input">The input model to render.</param>
        /// <param name="flavor">SQL Server or Azure SQL.</param>
        /// <returns>The parameterized procedure call.</returns>
        public RenderedQuery Render(TInput input, DbFlavor flavor) => CreateQuery(input).Render(flavor);

        private CallableQuery<TInput, TOutput> CreateQuery(TInput input)
            => new CallableQuery<TInput, TOutput>(_procedure, input);
    }
}
