﻿using System;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Where;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines additional join conditions on the child table
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class SqlConditionChildConstant : SqlCondition
    {
        /// <summary>
        /// The target property
        /// </summary>
        public readonly string ChildProperty;
        /// <summary>
        /// The value to compare against
        /// </summary>
        public readonly object Value;
        /// <summary>
        /// The operator
        /// </summary>
        public readonly SqlOp Op;

        /// <summary>
        /// Defines additional join conditions on the child table
        /// </summary>
        /// <param name="child_property_name">The name of child table column property/field to compare against</param>
        /// <param name="value">The constant value to compare</param>
        /// <param name="op">The operator</param>
        /// <param name="apply_to_idx">The index of the join to apply to in the relationship</param>
        public SqlConditionChildConstant(string child_property_name, object value, SqlOp op, int apply_to_idx = 0) : base(apply_to_idx)
        {
            ChildProperty = child_property_name;
            Value = value;
            Op = op;
        }

        /// <inheritdoc/>
        protected internal override void AddToJoin(Relationship relationship, Join join)
        {
            var column = join.Child.Columns.TryGet(ChildProperty);
            if (column == null)
                throw new FormatException($"Cannot find column '{this.ChildProperty}' on table '{join.Child.Name}'.");

            var condition = new WhereColumnValue(column, Value, this.Op, SqlSubQueryOp.ANY);
            join.Condition.AddComponent(condition);
            join.AdditionalCondition.AddComponent(condition);
        }
    }
}