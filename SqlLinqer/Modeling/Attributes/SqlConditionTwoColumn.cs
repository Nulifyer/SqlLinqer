using System;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Where;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines a constant condition against a root column to add to the join
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class SqlConditionTwoColumn : SqlCondition
    {
        /// <summary>
        /// The column on the data target/left model
        /// </summary>
        public readonly string LeftProperty;

        /// <summary>
        /// The column on the joined target/right model
        /// </summary>
        public readonly string RightProperty;

        /// <summary>
        /// The operator
        /// </summary>
        public readonly SqlOp Op;

        /// <summary>
        /// Defines a constant condition against a root column to add to the join
        /// </summary>
        /// <param name="left_property">The name of the property on the data target table</param>
        /// <param name="right_property">The name of the property on the child column table</param>
        /// <param name="op">The operator</param>
        /// <param name="apply_to_idx">The index of the join to apply to in the relationship</param>
        public SqlConditionTwoColumn(string left_property, string right_property, SqlOp op, int apply_to_idx = 0) : base(apply_to_idx)
        {
            LeftProperty = left_property;
            RightProperty = right_property;
            Op = op;
        }

        /// <inheritdoc/>
        protected internal override void AddToJoin(Relationship relationship, Join join)
        {
            var left = join.Parent.Columns.TryGet(LeftProperty);            
            if (left == null)
                throw new FormatException($"Cannot find column '{this.LeftProperty}' on table '{join.Parent.Name}'.");
            
            var right = join.Child.Columns.TryGet(RightProperty);
            if (right == null)
                throw new FormatException($"Cannot find column '{this.RightProperty}' on table '{join.Child.Name}'.");

            var condition = new WhereTwoColumn(left, right, this.Op);
            join.Condition.AddComponent(condition);
            join.AdditionalCondition.AddComponent(condition);
        }
    }
}
