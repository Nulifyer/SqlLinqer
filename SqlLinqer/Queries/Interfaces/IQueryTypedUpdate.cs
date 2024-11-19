using System;
using System.Linq.Expressions;
using SqlLinqer.Components.Update;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed update query
    /// </summary>
    public interface IQueryTypedUpdate<TQuery, TObj>
    {
        /// <summary>
        /// Get the set of columns and values for the query from the object.
        /// </summary>
        /// <param name="obj">The object get the columns and values from</param>
        TQuery UpdateNonDefaults(TObj obj);
        /// <summary>
        /// Get the set of columns and values for the query from the object.
        /// The updated columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoUpdate"/> 
        /// and <see cref="SqlLinqer.Modeling.SqlAutoUpdateExclude"/> attributes.
        /// </summary>
        /// <param name="obj">The object get the columns and values from</param>
        TQuery UpdateAuto(TObj obj);
        /// <summary>
        /// Add an update statement of a column to a value
        /// </summary>
        /// <typeparam name="TProperty">The type of the targeted property</typeparam>
        /// <param name="expression">The expression target of the column</param>
        /// <param name="value">The value to set</param>
        TQuery Update<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value);
        /// <summary>
        /// Add an update statement of a column to the result of an operation
        /// </summary>
        /// <typeparam name="TProperty">The type of the targeted property</typeparam>
        /// <param name="expression">The expression target of the column</param>
        /// <param name="operation">The operation to perform and set</param>
        TQuery Update<TProperty>(Expression<Func<TObj, TProperty>> expression, UpdateOperation operation);
        /// <summary>
        /// Add an update statement of a column to the value of another column
        /// </summary>
        /// <typeparam name="TProperty">The type of the targeted property</typeparam>
        /// <param name="expression_update_column">The left expression target of the column</param>
        /// <param name="expression_value_column">The right expression target of the column</param>
        TQuery Update<TProperty>(Expression<Func<TObj, TProperty>> expression_update_column, Expression<Func<TObj, TProperty>> expression_value_column);

    }
}