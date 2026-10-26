using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines a SQL Server or Azure SQL procedure through SqlProcedure, without an input model.
    /// Each execution returns one row set and standard response metadata. Standard total-count outputs are detected automatically.
    /// </summary>
    /// <typeparam name="TOutput">The returned row model or scalar type.</typeparam>
    public abstract partial class SqlLinqerCallable<TOutput>
    {
        private static readonly object NoInput = new object();
        private readonly SqlProcedure _procedure;
        private readonly BaseConnector _connector;

        /// <summary>Reads the SqlProcedure mapping from the derived class without executing SQL.</summary>
        /// <param name="connector">An optional retained connector. Procedure name and schema come from SqlProcedure.</param>
        protected SqlLinqerCallable(BaseConnector connector = null)
        {
            _procedure = SqlProcedureMapping.Get(GetType());
            _connector = connector;
        }

        /// <summary>Renders the procedure call without input parameters or opening a connection.</summary>
        /// <param name="connector">The rendering connector, or null to use the retained or current default connector.</param>
        /// <returns>The procedure call and standard metadata bindings.</returns>
        public RenderedQuery Render(BaseConnector connector = null)
            => CreateQuery().Render(connector ?? _connector ?? Default.Connector);

        /// <summary>Renders the procedure call for the selected dialect without opening a connection.</summary>
        /// <param name="flavor">SQL Server or Azure SQL.</param>
        /// <returns>The procedure call and standard metadata bindings.</returns>
        public RenderedQuery Render(DbFlavor flavor) => CreateQuery().Render(flavor);

        private CallableQuery<object, TOutput> CreateQuery()
            => new CallableQuery<object, TOutput>(_procedure, NoInput);
    }
}
