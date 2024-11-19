using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Ordering
{
    public interface IOrderByStatement
    {
        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The query current query</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor);
    }
}