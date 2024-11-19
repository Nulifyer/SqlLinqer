using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Queries.Core.Abstract
{
    /// <summary>
    /// The base structue of a core query.
    /// </summary>
    public abstract class TableQuery : BaseQuery
    {
        protected internal readonly Table Table;
        
        /// <summary>
        /// The base structue of a query targeting a table.
        /// </summary>
        public TableQuery(Table table)
        { 
            Table = table;
        }
    }
}