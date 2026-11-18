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
        public Column UpdateColumn { get; protected set; }

        /// <summary>
        /// Renders the left side of the assignment for the target database. PostgreSQL, SQLite, and DuckDB require a bare
        /// column name there; SQL Server, Azure SQL, and MySQL accept the alias-qualified column.
        /// Use it for the assignment target in a custom <see cref="UpdateOperation"/>, and <see cref="UpdateColumn"/>'s
        /// Render for the column's existing value on the right side.
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <returns>The assignment target, ready to be followed by an equals sign.</returns>
        public string RenderTarget(DbFlavor flavor)
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
