using System;
using System.Linq;
using System.Collections.Generic;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Render
{
    /// <summary>
    /// A collection of parameters
    /// </summary>
    public class ParameterCollection : ParameterCollectionBase
    {
        /// <summary>
        /// The param id tracker
        /// </summary>
        protected int ParamCount;

        /// <summary>
        /// A collection of parameters
        /// </summary>
        public ParameterCollection() : base()
        {
            ParamCount = 0;
        }

        private string NextId()
        {
            ParamCount += 1;
            return $"@P{ParamCount}";
        }

        /// <summary>
        /// Add a parameter to the collection
        /// </summary>
        /// <param name="value">The value of the parameter</param>
        /// <param name="dbType">The <see cref="System.Data.DbType"/> of the parameter</param>
        /// <returns>The placeholder string of the parameter</returns>
        public string AddParameter(object value, System.Data.DbType? dbType = null)
        {
            string id = NextId();
            base.AddParameter(id, value, dbType);
            return id;
        }
        /// <summary>
        /// Add a TVP parameter to the collection
        /// </summary>
        /// <param name="value">The <see cref="System.Data.DataTable"/> value with the data</param>
        /// <param name="typeName">The Sql type name of the data</param>
        /// <returns>The placeholder string of the parameter</returns>
        public string AddTvpParameter(System.Data.DataTable value, string typeName)
        {
            string id = NextId();
            base.AddTvpParameter(id, value, typeName);
            return id;
        }
    }
}