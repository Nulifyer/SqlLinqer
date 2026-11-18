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
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <returns>The assignment target, ready to be followed by an equals sign.</returns>
        public string RenderTarget(DbFlavor flavor)
        {
            return (flavor == DbFlavor.PostgreSql || flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb) ? UpdateColumn.ColumnName.DbWrap(flavor) : UpdateColumn.Render(flavor);
        }

        /// <summary>
        /// Renders the value that the column receives, without the assignment target.
        /// </summary>
        /// <param name="query">The update being rendered. It is null when the statement belongs to an insert's conflict action.</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <returns>The SQL expression on the right side of the assignment.</returns>
        public abstract string RenderValue(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor);

        /// <summary>
        /// Renders the whole assignment: the target, an equals sign, and the value.
        /// </summary>
        /// <param name="query">The update being rendered. It is null when the statement belongs to an insert's conflict action.</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <returns>The rendered assignment.</returns>
        public string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
            => $"{RenderTarget(flavor)} = {RenderValue(query, parameters, flavor)}";
    }
}
