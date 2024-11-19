using SqlLinqer.Components.Render;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// An update statement that updates one column with another
    /// </summary>
    public class ColumnOperationUpdate : UpdateStatement
    {        
        /// <summary>
        /// Value Column
        /// </summary>
        public readonly UpdateOperation Operation;

        /// <summary>
        /// An update statement that updates one column with another
        /// </summary>
        /// <param name="update_column">The column to update</param>
        /// <param name="operation">The operation to perform</param>
        public ColumnOperationUpdate(Column update_column, UpdateOperation operation)
        {
            UpdateColumn = update_column;
            Operation = operation;
        }

        /// <inheritdoc/>
        public override string Render(UpdateQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            return Operation.Render(query, this, parameters, flavor);
        }
    }
}