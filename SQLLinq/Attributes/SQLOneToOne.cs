using System;

namespace SqlLinqer
{
    /// <summary>
    /// Used to label one to one realtionships
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SQLOneToOne : Attribute
    {
        /// <summary>
        /// The name of the class's property or field that is a the foreign key
        /// </summary>
        public string TargetProp { get; private set; }

        /// <summary>
        /// Marks property/field as a one to one relationship
        /// </summary>
        /// <param name="property_name">The name of this class's property/field that contains the realted foreign key</param>
        public SQLOneToOne(string property_name)
        {
            TargetProp = property_name;
        }
    }
}
