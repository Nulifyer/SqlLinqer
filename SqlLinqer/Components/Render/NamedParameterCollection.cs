using System;
using System.Linq;
using System.Collections.Generic;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Render
{
    /// <summary>
    /// A collection of named parameters
    /// </summary>
    public class NamedParameterCollection : ParameterCollectionBase
    {
        /// <summary>
        /// A collection of named parameters
        /// </summary>
        public NamedParameterCollection() : base()
        {

        }

        /// <summary>
        /// Add a parameter to the collection
        /// </summary>
        /// <param name="value">The value of the parameter</param>
        /// <param name="dbType">The <see cref="System.Data.DbType"/> of the parameter</param>
        public new void AddParameter(string name, object value, System.Data.DbType? dbType = null)
        {
            base.AddParameter(name, value, dbType);
        }
        /// <summary>
        /// Add a TVP parameter to the collection
        /// </summary>
        /// <param name="value">The <see cref="System.Data.DataTable"/> value with the data</param>
        /// <param name="typeName">The Sql type name of the data</param>
        public new void AddTvpParameter(string name, System.Data.DataTable value, string typeName)
        {
            base.AddTvpParameter(name, value, typeName);
        }
    }
}