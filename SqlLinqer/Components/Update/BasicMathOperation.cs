using System.ComponentModel;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>An arithmetic operation applied to an update assignment.</summary>
    public class BasicMathOperation : UpdateOperation
    {
        /// <summary>The arithmetic operator to apply.</summary>
        public readonly MathOp Operator;
        /// <summary>The operand applied to the current assignment value.</summary>
        public readonly object Value;

        /// <summary>Creates an arithmetic update operation.</summary>
        public BasicMathOperation(MathOp op, object value)
        {
            Operator = op;
            Value = value;
        }

        /// <inheritdoc/>
        public override string Render(UpdateQuery query, UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor)
        {
            string placeholder = parameters.AddParameter(this.Value);
            string columnStr = statement.UpdateColumn.Render(flavor);
            string opStr = Operator.GetDescription();
            return $"{statement.RenderTarget(flavor)} = {columnStr} {opStr} {placeholder}";
        }

        /// <summary>
        /// A enum of simple math operations
        /// </summary>
        public enum MathOp
        {
            /// <summary>
            /// Addition
            /// </summary>
            [Description("+")]
            Add,

            /// <summary>
            /// Subtraction
            /// </summary>
            [Description("-")]
            Sub,

            /// <summary>
            /// Multiplication
            /// </summary>
            [Description("*")]
            Mult,

            /// <summary>
            /// Division
            /// </summary>
            [Description("/")]
            Div,
        }
    }
}
