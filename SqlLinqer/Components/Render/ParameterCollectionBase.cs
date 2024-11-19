using System;
using System.Linq;
using System.Collections.Generic;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Components.Render
{
    /// <summary>
    /// A collection of parameters
    /// </summary>
    public class ParameterCollectionBase : KeyedComponentCollectionBase<string, Parameter>
    {
        /// <summary>
        /// A collection of parameters
        /// </summary>
        public ParameterCollectionBase() : base()
        {
            
        }

        /// <summary>
        /// Add a parameter to the collection
        /// </summary>
        /// <param name="id">The id of the parameter</param>
        /// <param name="value">The value of the parameter</param>
        /// <param name="dbType">The <see cref="System.Data.DbType"/> of the parameter</param>
        /// <returns>The placeholder string of the parameter</returns>
        protected void AddParameter(string id, object value, System.Data.DbType? dbType)
        {
            switch (value)
            {
                case null:
                    value = DBNull.Value;
                    break;
                case bool b:
                    value = b ? 1 : 0;
                    break;
                case DateTime dt:
                    value = dt;
                    break;
                case DateTimeOffset dto:
                    value = dto;
                    break;
                case System.Data.DataTable dt:
                    throw new System.ArgumentException($"Use {nameof(AddTvpParameter)} instead for {typeof(System.Data.DataTable).FullName} values.");
            }

            Components.Add(id, new Parameter(id, value, dbType));
        }
        /// <summary>
        /// Add a TVP parameter to the collection
        /// </summary>
        /// <param name="id">The id of the parameter</param>
        /// <param name="value">The <see cref="System.Data.DataTable"/> value with the data</param>
        /// <param name="typeName">The Sql type name of the data</param>
        /// <returns>The placeholder string of the parameter</returns>
        protected void AddTvpParameter(string id, System.Data.DataTable value, string typeName)
        {
            Components.Add(id, new TvpParameter(id, value, typeName));
        }
    
        protected internal void ImportParametersFrom(ParameterCollectionBase other_collection)
        {
            foreach (var parameter in other_collection.GetAll())
            {
                this.AddComponent(parameter.Placehodler, parameter);
            }
        }
    }
}