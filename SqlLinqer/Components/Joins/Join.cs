using System.Text;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Joins
{
    /// <summary>A join between two tables and its column predicates.</summary>
    public class Join
    {
        /// <summary>The parent table of the join.</summary>
        public readonly Table Parent;
        /// <summary>The child table of the join.</summary>
        public readonly Table Child;
        /// <summary>The predicates connecting the parent and child columns.</summary>
        public readonly WhereCollection Condition;
        /// <summary>Additional predicates appended to the join condition.</summary>
        public readonly WhereCollection AdditionalCondition;
        
        private bool _reversed;
        /// <summary>The child table depth, negated for a reversed join.</summary>
        public int Order => _reversed ? Child.Order * -1 : Child.Order;


        private Join()
        {
            _reversed = false;
            Condition = new WhereCollection(SqlWhereOp.AND);
            AdditionalCondition = new WhereCollection(SqlWhereOp.AND);
        }
        /// <summary>Creates a join from the supplied table mappings.</summary>
        public Join(Table root_table) : this()
        {
            Child = root_table;
        }
        /// <summary>Creates a join from the supplied table mappings.</summary>
        public Join(Table parent, Table child) : this()
        {
            Parent = parent;
            Child = child;
        }

        /// <summary>
        /// Returns a join with swapped parent and child tables and the same predicate components.
        /// Marks this join as reversed for ordering. The returned join has separate predicate collections.
        /// </summary>
        public Join GetReversed()
        {
            _reversed = true;
            var reversed = new Join(Child, Parent);
            reversed.Condition.ImportFrom(Condition);
            reversed.AdditionalCondition.ImportFrom(AdditionalCondition);
            return reversed;
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
        public virtual void Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {            
            if (Condition.Count > 0 && Child.UUID != query.Table.UUID)
            {
                builder.Append($"LEFT JOIN ");
            }
            builder.Append($"{Child.RenderFullName(flavor)} AS {Child.Alias.DbWrap(flavor)} ");
            // if has condition and is not the root table
            if (Condition.Count > 0 && Child.UUID != query.Table.UUID)
            {
                builder.Append($"ON {Condition.Render(parameters, flavor)} ");
            }
        }

        /// <summary>Returns a diagnostic description of this mapping.</summary>
        public override string ToString()
        {
            return $"{Child} <> {Parent}";
        }
    }
}
