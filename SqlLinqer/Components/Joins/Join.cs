using System.Text;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Joins
{
    public class Join
    {
        public readonly Table Parent;
        public readonly Table Child;
        public readonly WhereCollection Condition;
        public readonly WhereCollection AdditionalCondition;
        
        private bool _reversed;
        public int Order => _reversed ? Child.Order * -1 : Child.Order;


        private Join()
        {
            _reversed = false;
            Condition = new WhereCollection(SqlWhereOp.AND);
            AdditionalCondition = new WhereCollection(SqlWhereOp.AND);
        }
        public Join(Table root_table) : this()
        {
            Child = root_table;
        }
        public Join(Table parent, Table child) : this()
        {
            Parent = parent;
            Child = child;
        }

        public Join GetReversed()
        {
            _reversed = true;
            var reversed = new Join(Child, Parent);
            reversed.Condition.ImportFrom(Condition);
            reversed.AdditionalCondition.ImportFrom(AdditionalCondition);
            return reversed;
        }

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

        public override string ToString()
        {
            return $"{Child} <> {Parent}";
        }
    }
}