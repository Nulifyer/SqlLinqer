using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Components.Joins;
using System.Collections.Generic;

namespace SqlLinqer.Components.Where
{
    public class WhereTwoColumn : WhereStatement
    {
        public readonly Column Left;
        public readonly Column Right;
        public readonly SqlOp Operator;

        public WhereTwoColumn(Column left, Column right, SqlOp op)
        {
            Left = left;
            Right = right;
            Operator = op;
        }

        public override int Order => 150;

        public override IWhereComponent Invert()
        {
            return new WhereTwoColumn(Left, Right, Operator.GetInvert());
        }

        public override IWhereComponent Finalize(WhereQuery query)
        {
            if (query?.Withs == null)
                return this;

            var col_path = Left.GetPath(query.Table);
            var rel_many_left = query.Table.FindFirstManyRelationshipFromPath(col_path);

            col_path = Right.GetPath(query.Table);
            var rel_many_right = query.Table.FindFirstManyRelationshipFromPath(col_path);

            if (rel_many_left == null && rel_many_right == null)
                return this;
            
            Relationship rel_many;
            bool left_IsSameOrChildOf;
            bool right_IsSameOrChildOf;
                        
            if (rel_many_left == null ^ rel_many_right == null)
            {
                left_IsSameOrChildOf = false;
                right_IsSameOrChildOf = false;
            }
            else
            {
                left_IsSameOrChildOf = rel_many_left.ChildTable.IsSameOrChildOf(rel_many_right.ChildTable);
                right_IsSameOrChildOf = rel_many_right.ChildTable.IsSameOrChildOf(rel_many_left.ChildTable);
            }

            bool inverted;
            switch (Operator)
            {
                case SqlOp.NOT:
                case SqlOp.NOTLIKE:
                case SqlOp.NOTLIKEWORD:
                case SqlOp.NOTLIKEORNULL:
                case SqlOp.NOTLIKEWORDORNULL:
                    inverted = true;
                    break;
                default:
                    inverted = false;
                    break;
            }

            WhereWithJoinBack where_with;
            if (!left_IsSameOrChildOf && !right_IsSameOrChildOf)
            {
                where_with = new WhereWithJoinBack(query, query.Table, inverted);
            }
            else
            {
                where_with = new WhereWithJoinBack(query, left_IsSameOrChildOf ? rel_many_left : rel_many_right, inverted);
            }

            where_with.SubQuery.RootWhere.AddComponent(this);
            return where_with;
        }
        public override bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component) => false;

        public override void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            query?.Joins.AddTable(Left.Table);
            query?.Joins.AddTable(Right.Table);

            builder.Append($"{Left.Render(flavor)} {Operator.GetDescription()} {Right.Render(flavor)}");
        }
    
        public override IEnumerable<Column> GetColumnReferences()
        {
            yield return Left;
            yield return Right;
        }
    }
}