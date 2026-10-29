using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines a SQL Server or Azure SQL procedure through SqlProcedure, without an input model.
    /// Each execution returns an ordered tuple of result collections and execution metadata.
    /// </summary>
    /// <typeparam name="TResults">An ordered value tuple of supported collection types.</typeparam>
    public abstract partial class SqlLinqerMultiCallable<TResults>
    {
        private static readonly object NoInput = new object();
        private readonly SqlProcedure _procedure;
        private readonly BaseConnector _connector;

        /// <summary>Reads the SqlProcedure mapping from the derived class without executing SQL.</summary>
        /// <param name="connector">An optional retained connector. Procedure name and schema come from SqlProcedure.</param>
        protected SqlLinqerMultiCallable(BaseConnector connector = null)
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

        private MultiCallableQuery<object, TResults> CreateQuery()
            => new MultiCallableQuery<object, TResults>(_procedure, NoInput);
    }
}
