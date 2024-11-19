using System.ComponentModel;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    public class BasicMathOperation : UpdateOperation
    {
        public readonly MathOp Operator;
        public readonly object Value;
        
        public BasicMathOperation(MathOp op, object value)
        {
            Operator = op;
            Value = value;
        }
        
        public override string Render(UpdateQuery query, UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor)
        {
            string placeholder = parameters.AddParameter(this.Value);
            string columnStr = statement.UpdateColumn.Render(flavor);
            string opStr = Operator.GetDescription();
            return $"{columnStr} = {columnStr} {opStr} {placeholder}";
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