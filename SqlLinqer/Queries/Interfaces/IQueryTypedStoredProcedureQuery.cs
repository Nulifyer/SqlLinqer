namespace SqlLinqer.Queries.Interfaces
{
    /// <summary>
    /// A typed stored procedure query
    /// </summary>
    public interface IQueryTypedStoredProcedure<TQuery>
    {
        /// <summary>
        /// Adds a named scalar input to a legacy stored-procedure call. Use an explicit DbType when a null input needs a known SQL type.
        /// </summary>
        /// <param name="name">The procedure parameter name.</param>
        /// <param name="value">The bound input value, including SQL NULL.</param>
        /// <param name="db_type">An optional explicit parameter type.</param>
        /// <returns>The same mutable procedure query.</returns>
        TQuery AddParameter(string name, object value, System.Data.DbType? db_type = null);
        /// <summary>
        /// Adds a SQL Server or Azure SQL table-valued input using an existing database table type. It does not create or drop that type.
        /// Keeps the DataTable reference during ordinary rendering. Keep it unchanged during execution or Capture the query to copy its data.
        /// </summary>
        /// <param name="name">The procedure parameter name.</param>
        /// <param name="data_table">The input rows with column types and order matching the SQL type. An empty table represents zero rows.</param>
        /// <param name="type_name">The existing SQL table type name, including its schema when needed.</param>
        /// <returns>The same mutable procedure query.</returns>
        TQuery AddTvpParameter(string name, System.Data.DataTable data_table, string type_name);
    }
}