using SqlLinqer.Compilation;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Core.Abstract
{
    /// <summary>
    /// Provides SQL rendering and detached query capture for mutable query builders.
    /// Rendering opens no database connection. Finish builder changes before capture or execution.
    /// </summary>
    public abstract class BaseQuery : IQueryCore
    {
        internal BaseConnector BoundConnector { get; set; }
        internal bool IsCompilationCopy;

        /// <summary>
        /// Captures a detached query definition for repeated or concurrent rendering. Later builder changes do not change the snapshot.
        /// Finish builder mutation before capture. Arrays, binary buffers, and DataTables are copied; other provider-specific objects must remain immutable.
        /// </summary>
        /// <returns>A snapshot owning a copy of the query structure and supported mutable parameter values.</returns>
        /// <example>
        /// <code>
        /// var snapshot = query.Capture();
        /// var data = snapshot.Render(connector);
        /// var count = snapshot.RenderForCount(connector.DbFlavor);
        /// </code>
        /// </example>
        public QuerySnapshot Capture() => new QuerySnapshot(this);

        /// <summary>
        /// The connector supplied when the query was created, or the current default connector.
        /// </summary>
        protected BaseConnector ExecutionConnector => BoundConnector ?? SqlLinqer.Default.Connector;

        /// <summary>
        /// The base structue of a core query.
        /// </summary>
        public BaseQuery() { }
        
        /// <inheritdoc/>
        public RenderedQuery Render(DbFlavor flavor)
        {
            // Procedure rendering only reads its bindings. Preserve the existing TVP
            // identity contract; explicit Capture still freezes their mutable payloads.
            if (this is SqlLinqer.Queries.Core.StoredProcedureQuery)
            {
                var result = new RenderedQuery();
                result.Text = Render(result.Parameters, flavor);
                return result;
            }
            return QuerySnapshot.RenderQuery(this, flavor);
        }

        /// <inheritdoc/>
        public RenderedQuery Render(BaseConnector connector) => Render(connector.DbFlavor);

        /// <inheritdoc/>
        public abstract string Render(ParameterCollection existingCollection, DbFlavor flavor);
    }
}
