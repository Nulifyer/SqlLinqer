using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Exceptions;
using SqlLinqer.Extensions.MemberExpressionExtensions;

namespace SqlLinqer.Components.Select
{
    /// <summary>Builds typed WHEN and ELSE branches against a mapped model.</summary>
    public class CaseStatement<T> : CaseStatement
    {
        /// <summary>The root table used to resolve typed CASE expressions.</summary>
        protected internal readonly Table Table;
        /// <summary>Creates a CASE expression bound to a root table mapping and output alias.</summary>
        public CaseStatement(Table table, string alias) : base(alias)
        {
            Table = table;
        }
        /// <summary>Adds a WHEN branch returning the specified column.</summary>
        public CaseStatement<T> AddCaseColumn<TProperty>(Expression<Func<T, TProperty>> expression, Action<TableWhereCollection<T>> condition)
        {
            var column_path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            AddCaseValue(return_value: column, condition);
            return this;
        }
        /// <summary>Adds a WHEN branch returning the specified column.</summary>
        public CaseStatement<T> AddCaseColumn(Column column, Action<TableWhereCollection<T>> condition)
        {
            AddCaseValue(return_value: column, condition);
            return this;
        }
        /// <summary>Adds a WHEN branch returning a value, or an ELSE branch when the condition is null.</summary>
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
        /// <summary>Adds an ELSE branch returning the specified column.</summary>
        public CaseStatement<T> AddElseColumn<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            var column_path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(column_path);
            if (column == null)
                throw new SqlPathNotFoundException(Table, column_path);

            AddElseColumn(column);
            return this;
        }
        /// <summary>Adds an ELSE branch returning the specified column.</summary>
        public CaseStatement<T> AddElseColumn(Column column)
        {
            AddElseValue(return_value: column);
            return this;
        }
        /// <summary>Adds an ELSE branch returning the specified value.</summary>
        public CaseStatement<T> AddElseValue(object return_value)
        {
            AddCaseValue(return_value, null);
            return this;
        }
    }
}
