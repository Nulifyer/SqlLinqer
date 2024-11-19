using System.Collections.Generic;
using SqlLinqer.Exceptions;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Components.Where
{
    public class TableWhereCollection : WhereCollection
    {
        protected readonly Table Table;

        internal TableWhereCollection(Table table, SqlWhereOp group_op) : base(group_op)
        {
            Table = table;
        }

        protected void WhereColumnValue(IEnumerable<string> column_path, object value, SqlOp op, SqlSubQueryOp sub_op = SqlSubQueryOp.ANY)
        {
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            AddComponent(new WhereColumnValue(column, value, op, sub_op));            
        }
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

            if (computed_subop == SqlSubQueryOp.ANY)
            {
                AddComponent(new WhereColumnValue(column, values, computed_op, computed_subop));
            }
            else
            {
                var sub_group = new TableWhereCollection(Table, SqlWhereOp.AND);

                foreach (var val in values)
                {
                    sub_group.AddComponent(new WhereColumnValue(column, val, computed_op, computed_subop));
                }

                AddComponent(sub_group);
            }
        }
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