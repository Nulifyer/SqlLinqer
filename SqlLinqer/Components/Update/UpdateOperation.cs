using SqlLinqer.Components.Render;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// Computes the value an update assigns to a column, usually from the column's existing value.
    /// The library renders the assignment target, so an operation renders only the value.
    /// </summary>
    public abstract class UpdateOperation
    {
        /// <summary>
        /// Renders the value expression that is assigned to the statement's column.
        /// Use <see cref="UpdateStatement.UpdateColumn"/>'s Render for the column's existing value.
        /// </summary>
        /// <param name="statement">The assignment that contains this operation. Its UpdateColumn is the target column.</param>
        /// <param name="parameters">The bindings for the command. Add each application value as a parameter.</param>
        /// <param name="flavor">The target database.</param>
        /// <returns>The SQL expression on the right side of the assignment.</returns>
        public abstract string RenderValue(UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor);
    }
}
