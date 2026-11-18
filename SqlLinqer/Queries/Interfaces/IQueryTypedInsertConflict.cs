using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Update;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// Chooses what an insert does with a row whose unique key already exists.
    /// </summary>
    /// <typeparam name="TQuery">The query type being returned</typeparam>
    /// <typeparam name="TObj">The root table object</typeparam>
    public interface IQueryTypedInsertConflict<TQuery, TObj>
    {
        /// <summary>
        /// Names the unique key that identifies a conflicting row. Follow it with OnConflictUpdate or OnConflictDoNothing.
        /// MySQL applies whichever unique key collides and does not use these members.
        /// </summary>
        /// <param name="members">One or several root members that together form a primary or unique key.</param>
        TQuery OnConflict(params Expression<Func<TObj, object>>[] members);
        /// <summary>
        /// On a conflict, sets the column from the value this insert supplied for it.
        /// </summary>
        /// <typeparam name="TProperty">The property type</typeparam>
        /// <param name="expression">An inserted root member.</param>
        TQuery OnConflictUpdate<TProperty>(Expression<Func<TObj, TProperty>> expression);
        /// <summary>
        /// On a conflict, sets the column to a fixed value.
        /// </summary>
        /// <typeparam name="TProperty">The property type</typeparam>
        /// <param name="expression">The root member to assign.</param>
        /// <param name="value">The value to assign.</param>
        TQuery OnConflictUpdate<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value);
        /// <summary>
        /// On a conflict, computes the column from the existing row, such as adding to a counter with BasicMathOperation.
        /// </summary>
        /// <typeparam name="TProperty">The property type</typeparam>
        /// <param name="expression">The root member to assign.</param>
        /// <param name="operation">The operation applied to the existing row's value.</param>
        TQuery OnConflictUpdate<TProperty>(Expression<Func<TObj, TProperty>> expression, UpdateOperation operation);
        /// <summary>
        /// On a conflict, sets every inserted column that permits automatic updates from its inserted value.
        /// The conflict target, the primary key, and columns assigned explicitly are left alone.
        /// </summary>
        TQuery OnConflictUpdateAuto();
        /// <summary>
        /// Skips a row whose unique key already exists. The affected-row count and returned values cover only inserted rows.
        /// </summary>
        TQuery OnConflictDoNothing();
    }
}
