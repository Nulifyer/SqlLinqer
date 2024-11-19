using SqlLinqer.Components.Render;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// An update statement that updates one column with another
    /// </summary>
    public class ColumnColumnUpdate : UpdateStatement
    {        
        /// <summary>
        /// Value Column
        /// </summary>
        public readonly Column ValueColumn;

        /// <summary>
        /// An update statement that updates one column with another
        /// </summary>
        /// <param name="update_column">The column to update</param>
        /// <param name="value_column">The column to update from</param>
        public ColumnColumnUpdate(Column update_column, Column value_column)
        {
            UpdateColumn = update_column;
            ValueColumn = value_column;
        }

        /// <inheritdoc/>
        public override string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            return $"{UpdateColumn.Render(flavor)} = {ValueColumn.Render(flavor)}";
        }
    }
}