using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using SqlLinqer.Components.Where;

namespace SqlLinqer.Components.Where
{
    /// <summary>
    /// A object that represents a typed where collection
    /// </summary>
    /// <typeparam name="TReturn">The return query type.</typeparam>
    /// <typeparam name="TObj">The class object that the query is based on.</typeparam>
    public interface ITypedWhereCollection<TReturn, TObj>
    {
        /// <summary>
        /// Create a new instance of a where group
        /// </summary>
        /// <param name="where_op">The where group operator</param>
        TableWhereCollection<TObj> NewWhere(SqlWhereOp where_op);

        /// <summary>
        /// Add a where group to the query.
        /// </summary>
        /// <param name="component">The component to add to the group.</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where(IWhereComponent component);
        /// <summary>
        /// Add a where statement to the where group that targets column and value
        /// </summary>
        /// <typeparam name="TProperty">The targeted property type</typeparam>
        /// <param name="expression">The expression column target</param>
        /// <param name="value">The value to operate against the column</param>
        /// <param name="op">The operator</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlNull value, SqlOp op);
        /// <summary>
        /// Add a where statement to the where group that targets column and value
        /// </summary>
        /// <typeparam name="TProperty">The targeted property type</typeparam>
        /// <param name="expression">The expression column target</param>
        /// <param name="value">The value to operate against the column</param>
        /// <param name="op">The operator</param>
        /// <param name="subOp">If the statement generates a sub query, if the condition applies to ANY or ALL values of the sub query.</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp subOp);
        /// <summary>
        /// Add a where statement to the where group that targets column and value
        /// </summary>
        /// <typeparam name="TProperty">The targeted property type</typeparam>
        /// <param name="expression">The expression column target</param>
        /// <param name="value">The value to operate against the column</param>
        /// <param name="op">The operator</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value, SqlOp op);
        /// <summary>
        /// Add a where statement to the where group that targets column and value
        /// </summary>
        /// <typeparam name="TProperty">The targeted property type</typeparam>
        /// <param name="expression">The expression column target</param>
        /// <param name="value">The value to operate against the column</param>
        /// <param name="op">The operator</param>
        /// <param name="subOp">If the statement generates a sub query, if the condition applies to ANY or ALL values of the sub query.</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp subOp);
        /// <summary>
        /// Add a where statement to the where group that targets column and values
        /// </summary>
        /// <typeparam name="TProperty">The targeted property type</typeparam>
        /// <param name="expression">The expression column target</param>
        /// <param name="values">The values to operate against the column</param>
        /// <param name="op">The operator</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp op);
        /// <summary>
        /// Add a where statement to the where group that targets column and sub query
        /// </summary>
        /// <typeparam name="TProperty">The targeted property type</typeparam>
        /// <param name="expression">The expression column target</param>
        /// <param name="subquery">The sub query to run</param>
        /// <param name="op">The operator</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op);
        /// <summary>
        /// Add a where statement to the where group that targets two columns
        /// </summary>
        /// <typeparam name="TProperty">The targeted property type</typeparam>
        /// <param name="expression_left">The left expression column target</param>
        /// <param name="expression_right">The right expression column target</param>
        /// <param name="op">The operator</param>
        /// <returns>The current instance of the query</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression_left, Expression<Func<TObj, TProperty>> expression_right, SqlOp op);
    }
}