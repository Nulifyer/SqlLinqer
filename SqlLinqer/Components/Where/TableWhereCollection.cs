using System.Collections.Generic;
using System.Linq;
using SqlLinqer.Exceptions;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Components.Where
{
    /// <summary>Builds predicates by resolving member paths against a root table.</summary>
    public class TableWhereCollection : WhereCollection
    {
        /// <summary>The root table used to resolve predicate member paths.</summary>
        protected readonly Table Table;

        internal TableWhereCollection(Table table, SqlWhereOp group_op) : base(group_op)
        {
            Table = table;
        }

        /// <summary>Adds a comparison between a mapped column path and a value.</summary>
        protected void WhereColumnValue(IEnumerable<string> column_path, object value, SqlOp op, SqlSubQueryOp sub_op = SqlSubQueryOp.ANY)
        {
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            AddComponent(new WhereColumnValue(column, value, op, sub_op));            
        }
        /// <summary>Adds an array or subquery predicate for a mapped column path.</summary>
        protected void WhereColumnArrayValue(IEnumerable<string> column_path, IEnumerable<object> values, SqlArrayOp array_op)
        {
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            SqlOp computed_op;
            SqlSubQueryOp computed_subop;

            switch (array_op)
            {
                case SqlArrayOp.ANY:
                case SqlArrayOp.IN:
                    computed_op = SqlOp.EQ;
                    computed_subop = SqlSubQueryOp.ANY;
                    break;
                case SqlArrayOp.NOTALL:
                case SqlArrayOp.NOTIN:
                    computed_op = SqlOp.NOT;
                    computed_subop = SqlSubQueryOp.ALL;
                    break;
                case SqlArrayOp.ALL:
                    computed_op = SqlOp.EQ;
                    computed_subop = SqlSubQueryOp.ALL;
                    break;
                case SqlArrayOp.NOTANY:
                    computed_op = SqlOp.NOT;
                    computed_subop = SqlSubQueryOp.ANY;
                    break;
                default:
                    throw new System.NotSupportedException($"The operator {array_op} is not supported.");
            }

            var items = values.ToArray();
            if (items.Length == 0)
            {
                AddComponent(new WhereConstant(computed_subop == SqlSubQueryOp.ALL));
                return;
            }

            if (computed_subop == SqlSubQueryOp.ANY)
            {
                AddComponent(new WhereColumnValue(column, items, computed_op, computed_subop));
            }
            else
            {
                var sub_group = new TableWhereCollection(Table, SqlWhereOp.AND);

                foreach (var val in items)
                {
                    sub_group.AddComponent(new WhereColumnValue(column, val, computed_op, computed_subop));
                }

                AddComponent(sub_group);
            }
        }
        /// <summary>Adds an array or subquery predicate for a mapped column path.</summary>
        protected void WhereColumnArrayValue(IEnumerable<string> column_path, Queries.Core.SelectQuery subquery, SqlArrayOp array_op)
        {
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            SqlOp computed_op;
            SqlSubQueryOp computed_subop;

            switch (array_op)
            {
                case SqlArrayOp.ANY:
                case SqlArrayOp.IN:
                    computed_op = SqlOp.EQ;
                    computed_subop = SqlSubQueryOp.ANY;
                    break;
                case SqlArrayOp.NOTALL:
                case SqlArrayOp.NOTIN:
                    computed_op = SqlOp.NOT;
                    computed_subop = SqlSubQueryOp.ALL;
                    break;
                case SqlArrayOp.ALL:
                    computed_op = SqlOp.EQ;
                    computed_subop = SqlSubQueryOp.ALL;
                    break;
                case SqlArrayOp.NOTANY:
                    computed_op = SqlOp.NOT;
                    computed_subop = SqlSubQueryOp.ANY;
                    break;
                default:
                    throw new System.NotSupportedException($"The operator {array_op} is not supported.");
            }

            AddComponent(new WhereColumnValue(column, subquery, computed_op, computed_subop));
        }
        /// <summary>Adds a comparison between two mapped column paths.</summary>
        protected void WhereTwoColumn(IEnumerable<string> column_path_left, IEnumerable<string> column_path_right, SqlOp op)
        {
            var column_left = Table.FindColumnFromPath(column_path_left);
            if (column_left == null)
                throw new SqlPathNotFoundException(Table, column_path_left);

            var column_right = Table.FindColumnFromPath(column_path_right);
            if (column_right == null)
                throw new SqlPathNotFoundException(Table, column_path_right);

            AddComponent(new WhereTwoColumn(column_left, column_right, op));
        }
    }
}
