using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Components.Insert
{
    /// <summary>
    /// A value to be inserted into a table. This holds the code type and Sql type of the data.
    /// </summary>
    public class InsertRowValue
    {
        /// <summary>
        /// The Sql name of the column
        /// </summary>
        public string Column { get; }
        /// <summary>
        /// The DbType of the data
        /// </summary>
        public System.Data.DbType? DbType { get; }
        /// <summary>
        /// The value to be inserted
        /// </summary>
        public object Value { get; }

        /// <summary>
        /// A value to be inserted into a table. This holds the code type and Sql type of the data.
        /// </summary>        
        /// <param name="column">The name of the Sql column</param>
        /// <param name="value">The value to be inserted</param>
        /// <param name="dbType">The Sql type of the data</param>
        public InsertRowValue(string column, object value, System.Data.DbType? dbType)
        {
            Column = column;
            Value = value;
            try
            {
                DbType = dbType ?? value?.GetType().GetDbType();
            }
            catch { }
        }
    }
}