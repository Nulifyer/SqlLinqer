using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// Assigns a value only when the column is currently NULL, and keeps an existing value otherwise.
    /// Use it for a value that is recorded once, such as the time work first started.
    /// </summary>
    public class SetIfNullOperation : UpdateOperation
    {
        /// <summary>
        /// The value assigned when the column is NULL
        /// </summary>
        public readonly object Value;

        /// <summary>
        /// Assigns a value only when the column is currently NULL
        /// </summary>
        /// <param name="value">The value assigned when the column is NULL</param>
        public SetIfNullOperation(object value)
        {
            Value = value;
        }

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The current query</param>
        /// <param name="statement">The update statement this is a part of</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public override string Render(UpdateQuery query, UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor)
        {
            string placeholder = parameters.AddParameter(this.Value);
            string columnStr = statement.UpdateColumn.Render(flavor);
            return $"{statement.RenderTarget(flavor)} = COALESCE({columnStr}, {placeholder})";
        }
    }
}
