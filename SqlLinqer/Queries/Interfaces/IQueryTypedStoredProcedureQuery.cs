namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed stored procedure query
    /// </summary>
    public interface IQueryTypedStoredProcedure<TQuery>
    {
        /// <summary>
        /// Add a parameter to the collection
        /// </summary>
        /// <param name="name">The name of the parameter</param>
        /// <param name="value">The value of the parameter</param>
        /// <param name="db_type">The <see cref="System.Data.DbType"/> of the parameter</param>
        TQuery AddParameter(string name, object value, System.Data.DbType? db_type = null);
        /// <summary>
        /// Add a TVP parameter to the collection
        /// </summary>
        /// <param name="name">The name of the parameter</param>
        /// <param name="data_table">The <see cref="System.Data.DataTable"/> value with the data</param>
        /// <param name="type_name">The Sql type name of the data</param>
        TQuery AddTvpParameter(string name, System.Data.DataTable data_table, string type_name);
    }
}