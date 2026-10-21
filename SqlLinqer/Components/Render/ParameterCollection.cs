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

        /// <summary>
        /// Creates a parameter collection using the selected dialect's placeholder syntax.
        /// Choose the dialect before adding parameters; populated collections cannot switch syntax families.
        /// </summary>
        /// <param name="flavor">The dialect. DuckDB uses $P placeholders; other current dialects use @P placeholders.</param>
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
        /// Adds a bound value and returns its SQL placeholder. Null is stored as DBNull.Value.
        /// </summary>
        /// <param name="value">The application value to bind. Use AddTvpParameter for DataTable values.</param>
        /// <param name="dbType">An optional explicit provider parameter type.</param>
        /// <returns>A generated placeholder that does not collide with existing bindings.</returns>
        public string AddParameter(object value, System.Data.DbType? dbType = null)
        {
            string id = NextId();
            base.AddParameter(id, value, dbType);
            return id;
        }

        internal string AddCallableParameter(object value, System.Data.DbType? dbType,
            System.Data.ParameterDirection direction, SqlLinqer.Modeling.SqlParameterAttribute metadata)
        {
            string id = NextId();
            AddComponent(id, new CallableParameter(id, value ?? DBNull.Value, dbType, direction, metadata));
            return id;
        }
        /// <summary>
        /// Adds a Structured parameter for an existing SQL Server table type. The payload remains a reference to the supplied DataTable.
        /// Does not create or drop the database type. Capture the query to copy supported mutable input data.
        /// </summary>
        /// <param name="value">A table matching the existing SQL type's column order and CLR types.</param>
        /// <param name="typeName">The existing type name, including its schema when required.</param>
        /// <returns>The generated placeholder for this parameter.</returns>
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
