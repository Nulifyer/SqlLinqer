using System;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Defines the class's table name
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SqlTable : Attribute
    {
        /// <summary>
        /// The database table name
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The database schema name
        /// </summary>
        public readonly string Schema;

        /// <summary>
        /// Defines the class's database table
        /// </summary>
        /// <param name="name">The database table name</param>
        /// <param name="schema">The database schema name</param>
        public SqlTable(string name, string schema = null)
        {
            Name = name;
            Schema = schema;
        }
    }
}
