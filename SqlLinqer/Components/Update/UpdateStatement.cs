using SqlLinqer.Components.Render;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Core;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// An update statement
    /// </summary>
    public abstract class UpdateStatement
    {
        /// <summary>
        /// Value Column
        /// </summary>
        public Column UpdateColumn { get; protected set;}

        internal string RenderTarget(DbFlavor flavor)
        {
            return (flavor == DbFlavor.PostgreSql || flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb) ? UpdateColumn.ColumnName.DbWrap(flavor) : UpdateColumn.Render(flavor);
        }

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The current query</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public abstract string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor);
    }
}
