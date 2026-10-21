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
        /// Adds non-primary-key assignments from the object, skipping nulls unless marked SqlUpdateDefaultAlways. Values such as zero and false are included.
        /// Does not apply SqlAutoUpdate or SqlAutoUpdateExclude filtering.
        /// </summary>
        /// <param name="obj">The instance whose current mapped values supply the assignments.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery UpdateNonDefaults(TObj obj);
        /// <summary>
        /// Adds assignments allowed by SqlAutoUpdate and SqlAutoUpdateExclude, including null values. Primary keys are never assigned automatically.
        /// Without a WHERE filter, execution updates every root-table row.
        /// </summary>
        /// <param name="obj">The model instance supplying the assignment values.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery UpdateAuto(TObj obj);
        /// <summary>
        /// Assigns a value, including SQL NULL, to a mapped root-table column.
        /// Without a WHERE filter, execution updates every root-table row.
        /// </summary>
        /// <typeparam name="TProperty">The target member type.</typeparam>
        /// <param name="expression">The column to assign. It must belong to the root model.</param>
        /// <param name="value">The value to bind as a parameter. Pass null to clear a nullable column.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        /// <exception cref="System.FormatException">The target expression refers to a related table rather than the root table.</exception>
        TQuery Update<TProperty>(Expression<Func<TObj, TProperty>> expression, TProperty value);
        /// <summary>
        /// Assigns the result of an update operation to a mapped root-table column.
        /// Without a WHERE filter, execution updates every root-table row.
        /// </summary>
        /// <typeparam name="TProperty">The target member type.</typeparam>
        /// <param name="expression">The column to assign. It must belong to the root model.</param>
        /// <param name="operation">The SQL assignment operation, such as BasicMathOperation.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        /// <exception cref="System.FormatException">The target expression refers to a related table rather than the root table.</exception>
        TQuery Update<TProperty>(Expression<Func<TObj, TProperty>> expression, UpdateOperation operation);
        /// <summary>
        /// Assigns the value of another mapped column to a root-table column, resolving joins for the source expression.
        /// DuckDB requires a mapped target primary key and rejects multiple source matches for one target key.
        /// </summary>
        /// <typeparam name="TProperty">The common member type.</typeparam>
        /// <param name="expression_update_column">The root-table column to assign.</param>
        /// <param name="expression_value_column">The source column on the root or a related model.</param>
        /// <returns>The same mutable query builder, so further calls can extend the query.</returns>
        TQuery Update<TProperty>(Expression<Func<TObj, TProperty>> expression_update_column, Expression<Func<TObj, TProperty>> expression_value_column);

    }
}
