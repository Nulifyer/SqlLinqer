using SqlLinqer.Components.Render;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// An update statement that updates one column with another
    /// </summary>
    public class ColumnValueUpdate : UpdateStatement
    {        
        /// <summary>
        /// Value Column
        /// </summary>
        public readonly object Value;

        /// <summary>
        /// An update statement that updates one column with another
        /// </summary>
        /// <param name="update_column">The column to update</param>
        /// <param name="value">The value to set</param>
        public ColumnValueUpdate(Column update_column, object value)
        {
            UpdateColumn = update_column;
            Value = value;
        }

        /// <inheritdoc/>
        public override string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            string placeholder = parameters.AddParameter(Value);
            return $"{UpdateColumn.Render(flavor)} = {placeholder}";
        }
    }
}