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

        private DbFlavor _flavor;
        private readonly Dictionary<Tuple<object, int>, string> _expressionParameters = new Dictionary<Tuple<object, int>, string>();

        private readonly Dictionary<object, string> _renderedExpressions = new Dictionary<object, string>();

        internal void RememberExpression(object expression, string sql) => _renderedExpressions[expression] = sql;
        internal string FindExpression(object expression) => _renderedExpressions.TryGetValue(expression, out var sql) ? sql : null;

        internal string AddExpressionParameter(object expression, int argument, object value)
        {
            // DuckDB requires the same placeholder identity when a function is repeated
            // in SELECT, GROUP BY or ORDER BY, even if the argument values are equal.
            var key = Tuple.Create(expression, argument);
            if (_expressionParameters.TryGetValue(key, out var id) && HasKey(id))
                return id;
            id = AddParameter(value);
            _expressionParameters[key] = id;
            return id;
        }

        /// <summary>Creates a parameter collection for the selected SQL dialect.</summary>
        public ParameterCollection(DbFlavor flavor) : this()
        {
            SetFlavor(flavor);
        }

        internal void SetFlavor(DbFlavor flavor)
        {
            if ((_flavor == DbFlavor.DuckDb) != (flavor == DbFlavor.DuckDb)
                && Count > 0)
                throw new InvalidOperationException("A populated parameter collection cannot change parameter syntax. Create a collection for the target flavor before adding parameters.");
            _flavor = flavor;
        }

        private string NextId()
        {
            string id;
            do
            {
                ParamCount += 1;
                id = $"{(_flavor == DbFlavor.DuckDb ? "$" : "@")}P{ParamCount}";
            } while (HasKey(id));
            return id;
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
            if (_flavor == DbFlavor.DuckDb)
                throw new NotSupportedException("DuckDB does not support table-valued parameters.");
            string id = NextId();
            base.AddTvpParameter(id, value, typeName);
            return id;
        }
    }
}