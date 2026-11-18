using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Update;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Chooses what an insert does with a row whose unique key already exists.
    /// </summary>
    /// <typeparam name="TQuery">The fluent query type.</typeparam>
    /// <typeparam name="TObj">The model type whose mapped values are inserted.</typeparam>
    public interface IQueryTypedInsertConflict<TQuery, TObj>
    {
        /// <summary>
        /// Names the unique key that identifies a conflicting row. Follow it with OnConflictUpdate or OnConflictDoNothing.
        /// MySQL applies whichever unique key collides and does not use these members.
        /// </summary>
        /// <param name="members">One or several root members that together form a primary or unique key.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflict(params Expression<Func<TObj, object>>[] members);
        /// <summary>
        /// Names the unique constraint that identifies a conflicting row, instead of its key members. PostgreSQL only; MySQL ignores it,
        /// as it ignores key members. The other databases reject it when the command renders.
        /// </summary>
        /// <param name="name">The constraint name as the database stores it.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictConstraint(string name);
        /// <summary>
        /// On a conflict, assigns the column the value this insert supplied for it.
        /// </summary>
        /// <typeparam name="TProperty">The target member type.</typeparam>
        /// <param name="expression">The column to assign. It must be an inserted member of the root model.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictUpdate<TProperty>(Expression<Func<TObj, TProperty>> expression);
        /// <summary>
        /// On a conflict, assigns a value, including SQL NULL, to a mapped root-table column.
        /// </summary>
        /// <typeparam name="TProperty">The target member type.</typeparam>
        /// <param name="expression">The column to assign. It must belong to the root model.</param>
        /// <param name="value">The value to bind as a parameter. Pass null to clear a nullable column.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictUpdate<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value);
        /// <summary>
        /// On a conflict, assigns a server value to a mapped root-table column.
        /// </summary>
        /// <typeparam name="TProperty">The target member type.</typeparam>
        /// <param name="expression">The column to assign. It must belong to the root model.</param>
        /// <param name="value">Use SqlTime.NOW to assign the database server's current time.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictUpdate<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlTime value);
        /// <summary>
        /// On a conflict, assigns the result of an update operation to a mapped root-table column.
        /// </summary>
        /// <typeparam name="TProperty">The target member type.</typeparam>
        /// <param name="expression">The column to assign. It must belong to the root model.</param>
        /// <param name="operation">The SQL assignment operation, such as BasicMathOperation. It reads the existing row's value.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictUpdate<TProperty>(Expression<Func<TObj, TProperty>> expression, UpdateOperation operation);
        /// <summary>
        /// Updates a conflicting row only when the existing row matches a filter. A row that does not match is left unchanged
        /// and is not returned or counted. Several calls combine with AND. MySQL has no conditional update: it keeps each column's
        /// existing value instead, and can assign at most one of the columns the condition reads.
        /// </summary>
        /// <param name="filter">Adds predicates on the existing row's root members.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictWhere(Action<SqlLinqer.Components.Where.TableWhereCollection<TObj>> filter);
        /// <summary>
        /// Updates a conflicting row only when the existing row's value compares with the inserted value as given,
        /// with the existing value on the left. For example, SqlOp.LT on a version member keeps a newer stored row.
        /// A NULL on either side does not match.
        /// </summary>
        /// <typeparam name="TProperty">The compared member type.</typeparam>
        /// <param name="expression">The column to compare. It must be an inserted member of the root model.</param>
        /// <param name="op">The comparison between the existing value and the inserted value.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictWhere<TProperty>(Expression<Func<TObj, TProperty>> expression, SqlOp op);
        /// <summary>
        /// On a conflict, assigns every inserted column that permits automatic updates the value this insert supplied.
        /// The conflict target, the primary key, and columns assigned explicitly are left alone.
        /// </summary>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictUpdateAuto();
        /// <summary>
        /// Skips a row whose unique key already exists. The affected-row count and returned values cover only inserted rows.
        /// </summary>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery OnConflictDoNothing();
    }
}
