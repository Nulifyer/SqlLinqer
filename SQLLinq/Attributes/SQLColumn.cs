using System;

namespace SqlLinqer
{
    /// <summary>
    /// Used to define a column name that is different from the property/field name
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SQLColumn : Attribute
    {
        /// <summary>
        /// The database name for this property/field
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Used to define the database name of a property/field name
        /// </summary>
        /// <param name="name">The database column name</param>
        public SQLColumn(string name)
        {
            Name = name;
        }
    }
}
