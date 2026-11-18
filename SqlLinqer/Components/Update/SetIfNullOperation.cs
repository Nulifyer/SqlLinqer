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

        /// <inheritdoc/>
        public override string RenderValue(UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor)
        {
            string placeholder = parameters.AddParameter(this.Value);
            return $"COALESCE({statement.UpdateColumn.Render(flavor)}, {placeholder})";
        }
    }
}
