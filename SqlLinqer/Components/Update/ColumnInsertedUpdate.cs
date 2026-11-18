using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// An assignment in an insert's conflict action that sets a column from the value the insert supplied for it.
    /// </summary>
    public class ColumnInsertedUpdate : UpdateStatement
    {
        /// <summary>
        /// An assignment that sets a column from the value the insert supplied for it
        /// </summary>
        /// <param name="update_column">The inserted column to update</param>
        public ColumnInsertedUpdate(Column update_column)
        {
            UpdateColumn = update_column;
        }

        /// <inheritdoc/>
        public override string RenderValue(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
            => RenderInserted(UpdateColumn, flavor);

        // How a conflict action names the value this insert supplied for a column.
        internal static string RenderInserted(Column column, DbFlavor flavor)
        {
            string name = column.ColumnName.DbWrap(flavor);
            if (flavor == DbFlavor.SqlServer || flavor == DbFlavor.AzureSql)
                return $"{SqlLinqer.Components.Insert.InsertConflict.SourceAlias.DbWrap(flavor)}.{name}";
            return flavor == DbFlavor.MySql ? $"VALUES({name})" : $"EXCLUDED.{name}";
        }
    }
}
