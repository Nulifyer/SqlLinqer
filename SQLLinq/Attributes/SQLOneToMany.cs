using System;

namespace SqlLinqer
{
    /// <summary>
    /// Used to label one to many realtionships
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SQLOneToMany : Attribute
    {
        /// <summary>
        /// The name of the class's property or field that is a the foreign key
        /// </summary>
        public string TargetProp { get; private set; }

        /// <summary>
        /// Marks property/field as a one to many relationship
        /// </summary>
        /// <param name="property_name">The name of joined class's property/field where the current class's primary key is the foreign key</param>
        public SQLOneToMany(string property_name)
        {
            TargetProp = property_name;
        }
    }
}
