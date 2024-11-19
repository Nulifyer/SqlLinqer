using SqlLinqer.Connections;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A core level query. Core queries are very open ended and do not have a lot of checks and balances. 
    /// They are only one step above using strings.
    /// </summary>
    public interface IQueryCore
    {
        /// <summary>
        /// Renders the query.
        /// </summary>
        /// <param name="connector">The connector that is being used to execute the query</param>
        RenderedQuery Render(BaseConnector connector);
        /// <summary>
        /// Renders the query.
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        RenderedQuery Render(DbFlavor flavor);
        /// <summary>
        /// Renders the query into a string.
        /// </summary>
        /// <param name="existingCollection">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        string Render(ParameterCollection existingCollection, DbFlavor flavor);
    }
}