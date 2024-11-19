
using System.Linq;
using SqlLinqer.Components.Render;
using System.Collections.Generic;

namespace SqlLinqer.Components.Insert
{
    /// <summary>
    /// Represents a row of data to be inserted into a table
    /// </summary>
    public class InsertRow
    {
        /// <summary>
        /// The list of values in the row
        /// </summary>
        public List<InsertRowValue> Values { get; }

        /// <summary>
        /// Represents a row of data to be inserted into a table
        /// </summary>
        public InsertRow()
        {
            Values = new List<InsertRowValue>();
        }

        /// <summary>
        /// Add a value to the row
        /// </summary>
        /// <param name="column">The name of the Sql column</param>
        /// <param name="value">The value to be inserted</param>
        /// <param name="dbType">The Sql type of the data</param>
        public void AddValue(string column, object value, System.Data.DbType? dbType)
        {
            Values.Add(new InsertRowValue(column, value, dbType));
        }

        /// <summary>
        /// Returns the row as a rendered Sql string
        /// </summary>
        /// <param name="existingCollection">The parameter collection being used for the current query</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public virtual string Render(ParameterCollection existingCollection, DbFlavor flavor)
        {
            var placeholders = Values
                .Select(v =>
                {
                    string placeholder = existingCollection.AddParameter(v.Value, v.DbType);
                    return placeholder;
                });
            return $"({string.Join(",", placeholders)})";
        }
    }
}