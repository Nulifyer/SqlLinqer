using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Exceptions;
using SqlLinqer.Extensions.MemberExpressionExtensions;

namespace SqlLinqer.Components.Select
{
    public class CaseStatement<T> : CaseStatement
    {
        protected internal readonly Table Table;
        public CaseStatement(Table table, string alias) : base(alias)
        {
            Table = table;
        }
        public CaseStatement<T> AddCaseColumn<TProperty>(Expression<Func<T, TProperty>> expression, Action<TableWhereCollection<T>> condition)
        {
            var column_path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            AddCaseValue(return_value: column, condition);
            return this;
        }
        public CaseStatement<T> AddCaseColumn(Column column, Action<TableWhereCollection<T>> condition)
        {
            AddCaseValue(return_value: column, condition);
            return this;
        }
        public CaseStatement<T> AddCaseValue(object return_value, Action<TableWhereCollection<T>> condition)
        {
            TableWhereCollection<T> where = null;
            if (condition != null)
            {
                where = new TableWhereCollection<T>(Table, SqlWhereOp.AND);
                condition.Invoke(where);
                Conditions.Add(new CaseCondition(where, return_value));
            }
            else
            {
                Conditions.Add(new CaseCondition(return_value));
            }
            return this;
        }
        public CaseStatement<T> AddElseColumn<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            var column_path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            AddElseColumn(column);
            return this;
        }
        public CaseStatement<T> AddElseColumn(Column column)
        {
            AddElseValue(return_value: column);
            return this;
        }
        public CaseStatement<T> AddElseValue(object return_value)
        {
            AddCaseValue(return_value, null);
            return this;
        }    
    }
}