using System.Collections.Generic;

namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed insert query
    /// </summary>
    public interface IQueryTypedInsert<TQuery, TObj>
    {
        /// <summary>
        /// Add the object as a row to the insert query.
        /// The inserted columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoInsert"/> 
        /// and <see cref="SqlLinqer.Modeling.SqlAutoInsertExclude"/> attributes.
        /// </summary>
        /// <param name="obj">The instance of the object to insert</param>
        /// <returns>The current instance of the query</returns>
        TQuery InsertAuto(TObj obj);
        /// <summary>
        /// Add the object as a row to the insert query.
        /// The inserted columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoInsert"/> 
        /// and <see cref="SqlLinqer.Modeling.SqlAutoInsertExclude"/> attributes.
        /// </summary>
        /// <param name="objs">A collection of objects to insert</param>
        /// <returns>The current instance of the query</returns>
        TQuery InsertAuto(IEnumerable<TObj> objs);
    }
}