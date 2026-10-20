namespace SqlLinqer
{
    /// <summary>
    /// Sql database/implementation type
    /// </summary>
    public enum DbFlavor
    {
        /// <summary>
        /// Microsoft Sql Server 2012 or newer
        /// </summary>
        SqlServer = 0,
        /// <summary>
        /// My Sql
        /// </summary>
        MySql = 1,
        /// <summary>
        /// PostgreSql
        /// </summary>
        PostgreSql = 2,
        /// <summary>
        /// Azure SQL Database, using the SQL Server dialect
        /// </summary>
        AzureSql = 5,
        /// <summary>SQLite 3.38 or newer with JSON support.</summary>
        Sqlite = 3,
    }
}
