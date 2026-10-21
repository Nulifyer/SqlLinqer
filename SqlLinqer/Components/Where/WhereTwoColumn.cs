using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Components.Joins;
using System.Collections.Generic;

namespace SqlLinqer.Components.Where
{
    /// <summary>Compares two mapped columns.</summary>
    public class WhereTwoColumn : WhereStatement
    {
        /// <summary>The left comparison column.</summary>
        public readonly Column Left;
        /// <summary>The right comparison column.</summary>
        public readonly Column Right;
        /// <summary>The comparison operator.</summary>
        public readonly SqlOp Operator;

        /// <summary>Creates a comparison between two columns.</summary>
        public WhereTwoColumn(Column left, Column right, SqlOp op)
        {
            Left = left;
            Right = right;
            Operator = op;
        }

        /// <summary>The preparation order used to sort predicates.</summary>
        public override int Order => 150;

        /// <inheritdoc/>
        public override IWhereComponent Invert()
        {
            return new WhereTwoColumn(Left, Right, Operator.GetInvert());
        }

        /// <inheritdoc/>
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
        /// <inheritdoc/>
        public override bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component) => false;

        /// <inheritdoc/>
        public override void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            query?.Joins.AddTable(Left.Table);
            query?.Joins.AddTable(Right.Table);

            builder.Append($"{Left.Render(flavor)} {Operator.GetDescription()} {Right.Render(flavor)}");
        }

        /// <inheritdoc/>
        public override IEnumerable<Column> GetColumnReferences()
        {
            yield return Left;
            yield return Right;
        }
    }
}
