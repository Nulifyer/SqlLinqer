using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using SqlLinqer.Components.Where;

namespace SqlLinqer.Components.Where
{
    /// <summary>
    /// Builds parameterized predicates and nested AND/OR groups against mapped model members.
    /// </summary>
    /// <typeparam name="TReturn">The fluent query or predicate-group type.</typeparam>
    /// <typeparam name="TObj">The root model for member expressions.</typeparam>
    public interface ITypedWhereCollection<TReturn, TObj>
    {
        /// <summary>
        /// Creates a detached AND/OR predicate group. Add the finished group with Where to apply it to the query.
        /// </summary>
        /// <param name="where_op">The logical operator combining predicates inside the new group.</param>
        /// <returns>A new mutable group bound to the same root model.</returns>
        /// <example>
        /// <code>
        /// var matches = query.NewWhere(SqlWhereOp.OR)
        ///     .Where(x =&gt; x.Name, pattern, SqlOp.LIKE)
        ///     .Where(x =&gt; x.Email, pattern, SqlOp.LIKE);
        /// query.Where(matches).Where(x =&gt; x.ManagerId, managerId, SqlOp.EQ);
        /// </code>
        /// </example>
        TableWhereCollection<TObj> NewWhere(SqlWhereOp where_op);

        /// <summary>
        /// Adds an existing predicate or group to the root conditions. A nested group preserves its own logical operator.
        /// </summary>
        /// <param name="component">The predicate or group to add.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where(IWhereComponent component);
        /// <summary>
        /// Adds a SQL NULL comparison against the mapped member.
        /// Relationship subqueries use ANY by default.
        /// </summary>
        /// <typeparam name="TProperty">The compared member type.</typeparam>
        /// <param name="expression">The mapped member to compare. Use First() to traverse a collection relationship.</param>
        /// <param name="value">Use SqlNull.NULL to test a mapped member for SQL NULL.</param>
        /// <param name="op">The comparison operator. LIKE patterns can contain % and _ wildcards.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlNull value, SqlOp op);
        /// <summary>
        /// Adds a SQL NULL comparison against the mapped member.
        /// </summary>
        /// <typeparam name="TProperty">The compared member type.</typeparam>
        /// <param name="expression">The mapped member to compare. Use First() to traverse a collection relationship.</param>
        /// <param name="value">Use SqlNull.NULL to test a mapped member for SQL NULL.</param>
        /// <param name="op">The comparison operator. LIKE patterns can contain % and _ wildcards.</param>
        /// <param name="subOp">ANY or ALL semantics when the comparison requires a relationship subquery.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlNull value, SqlOp op, SqlSubQueryOp subOp);
        /// <summary>
        /// Adds a parameterized comparison against a mapped member, including members reached through relationships.
        /// Relationship subqueries use ANY by default. EscapeForSqlLike escapes wildcards for literal search text.
        /// </summary>
        /// <typeparam name="TProperty">The compared member type.</typeparam>
        /// <param name="expression">The mapped member to compare. Use First() to traverse a collection relationship.</param>
        /// <param name="value">The comparison value. Application values are bound as parameters.</param>
        /// <param name="op">The comparison operator. LIKE patterns can contain % and _ wildcards.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value, SqlOp op);
        /// <summary>
        /// Adds a parameterized comparison against a mapped member, including members reached through relationships.
        /// EscapeForSqlLike escapes wildcards for literal search text.
        /// </summary>
        /// <typeparam name="TProperty">The compared member type.</typeparam>
        /// <param name="expression">The mapped member to compare. Use First() to traverse a collection relationship.</param>
        /// <param name="value">The comparison value. Application values are bound as parameters.</param>
        /// <param name="op">The comparison operator. LIKE patterns can contain % and _ wildcards.</param>
        /// <param name="subOp">ANY or ALL semantics when the comparison requires a relationship subquery.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value, SqlOp op, SqlSubQueryOp subOp);
        /// <summary>
        /// Compares a mapped member with a set of parameterized values. An empty IN matches no rows and an empty NOT IN matches every row.
        /// </summary>
        /// <typeparam name="TProperty">The compared member type.</typeparam>
        /// <param name="expression">The mapped member to compare.</param>
        /// <param name="values">The comparison values. An empty sequence is supported.</param>
        /// <param name="op">The set operator, such as IN or NOTIN.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, IEnumerable<TProperty> values, SqlArrayOp op);
        /// <summary>
        /// Compares a mapped member with values selected by a subquery.
        /// The subquery must project a compatible value. SQLite supports IN and NOT IN subqueries only.
        /// </summary>
        /// <typeparam name="TProperty">The compared member type.</typeparam>
        /// <param name="expression">The mapped member on the outer query.</param>
        /// <param name="subquery">The SELECT query supplying the comparison values.</param>
        /// <param name="op">The set or quantified-comparison operator.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression, Queries.Core.SelectQuery subquery, SqlArrayOp op);
        /// <summary>
        /// Compares two mapped columns, resolving the relationships needed by either expression.
        /// </summary>
        /// <typeparam name="TProperty">The common compared member type.</typeparam>
        /// <param name="expression_left">The left column expression.</param>
        /// <param name="expression_right">The right column expression.</param>
        /// <param name="op">The comparison operator.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TReturn Where<TProperty>(Expression<Func<TObj, TProperty>> expression_left, Expression<Func<TObj, TProperty>> expression_right, SqlOp op);
    }
}