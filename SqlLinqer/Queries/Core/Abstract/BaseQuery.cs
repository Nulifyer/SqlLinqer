using SqlLinqer.Compilation;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Core.Abstract
{
    /// <summary>
    /// The base structue of a core query.
    /// </summary>
    public abstract class BaseQuery : IQueryCore
    {
        internal BaseConnector BoundConnector { get; set; }
        internal bool IsCompilationCopy;

        /// <summary>Captures an isolated definition for repeated or concurrent rendering.</summary>
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
