using System;

namespace SqlLinqer
{
    /// <summary>
    /// Defines the class's table name
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SQLTable : Attribute
    {
        /// <summary>
        /// The database table name
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Defines the class's database table
        /// </summary>
        /// <param name="name">The database table name</param>
        public SQLTable(string name)
        {
            Name = name;
        }
    }
}
