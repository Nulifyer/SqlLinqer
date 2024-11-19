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
        /// <summary>
        /// The base structue of a core query.
        /// </summary>
        public BaseQuery() { }
        
        /// <inheritdoc/>
        public RenderedQuery Render(DbFlavor flavor)
        {
            var tempQuery = new RenderedQuery();
            tempQuery.Text = Render(tempQuery.Parameters, flavor);
            return tempQuery;
        }

        /// <inheritdoc/>
        public RenderedQuery Render(BaseConnector connector) => Render(connector.DbFlavor);

        /// <inheritdoc/>
        public abstract string Render(ParameterCollection existingCollection, DbFlavor flavor);
    }
}