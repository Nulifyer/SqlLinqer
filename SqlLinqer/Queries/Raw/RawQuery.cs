using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Raw
{
    /// <summary>
    /// Builds a command from hand-written SQL whose values are named with @name markers.
    /// Markers render as bound parameters for the target database. Text is never concatenated with values.
    /// </summary>
    public partial class RawQuery
        : BaseQuery
        , IQueryExecutable<int>
        , IAsyncQueryExecutable<int>
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ValueProperties = new ConcurrentDictionary<Type, PropertyInfo[]>();

        private readonly string _sql;
        private readonly List<NamedValue> _values = new List<NamedValue>();
        private bool _ignoreUnmatchedMarkers;
        private bool _withoutTransaction;

        /// <summary>Creates a command for the supplied SQL. This does not bind an execution connector.</summary>
        /// <param name="sql">SQL text that names its values with @name markers.</param>
        /// <param name="values">An optional object whose public properties supply the values, or a string-keyed dictionary.</param>
        public RawQuery(string sql, object values = null)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL text is required.", nameof(sql));
            _sql = sql;
            if (values != null) Values(values);
        }

        /// <summary>
        /// Adds one value for each public property of the object, such as an anonymous type, or for each entry of a string-keyed dictionary.
        /// A property name matches its @name marker without regard to case. An existing value with the same name is replaced.
        /// </summary>
        /// <param name="values">The object or string-keyed dictionary supplying the values.</param>
        /// <returns>This query.</returns>
        public RawQuery Values(object values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var pair in pairs) Value(pair.Key, pair.Value);
                return this;
            }
            if (values is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                    Value(entry.Key as string ?? throw new ArgumentException("A values dictionary must have string keys that name the SQL values.", nameof(values)), entry.Value);
                return this;
            }
            var type = values.GetType();
            if (type.IsPrimitive || type.IsEnum || values is string || values is decimal || values is DateTime
                || values is DateTimeOffset || values is Guid || values is System.Collections.IEnumerable)
                throw new ArgumentException("Pass an object whose properties name the SQL values, such as new { id }.", nameof(values));
            var properties = ValueProperties.GetOrAdd(type, t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToArray());
            foreach (var property in properties) Value(property.Name, property.GetValue(values));
            return this;
        }

        /// <summary>
        /// Sets the value for an @name marker. An existing value with the same name is replaced.
        /// Supply a database type for a null whose type the server cannot infer.
        /// </summary>
        /// <param name="name">The marker name, without the @ prefix.</param>
        /// <param name="value">The value to bind. Null binds a database NULL.</param>
        /// <param name="dbType">An optional explicit provider parameter type.</param>
        /// <returns>This query.</returns>
        public RawQuery Value(string name, object value, DbType? dbType = null)
            => SetValue(name, value, dbType, false);

        /// <summary>
        /// Sets a list of values for an @name marker. The marker renders one bound parameter for each element, separated by commas,
        /// so IN (@ids) becomes IN (@ids1, @ids2, @ids3). An existing value with the same name is replaced.
        /// An empty list is rejected when the command renders, because no replacement is correct for every use of the marker.
        /// </summary>
        /// <param name="name">The marker name, without the @ prefix.</param>
        /// <param name="values">The elements to bind. The list is copied. A string or byte array is one value, not a list.</param>
        /// <param name="dbType">An optional explicit provider parameter type for every element.</param>
        /// <returns>This query.</returns>
        public RawQuery ValueList(string name, System.Collections.IEnumerable values, DbType? dbType = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values is string || values is byte[])
                throw new ArgumentException("A string or byte array is one value. Use Value for it.", nameof(values));
            return SetValue(name, values.Cast<object>().ToArray(), dbType, true);
        }

        private RawQuery SetValue(string name, object value, DbType? dbType, bool isList)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name.Length > 0 && name[0] == '@') name = name.Substring(1);
            if (!IsMarkerName(name))
                throw new ArgumentException($"'{name}' is not a valid value name. Use letters, digits, and underscores, starting with a letter or underscore.", nameof(name));
            _values.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
            _values.Add(new NamedValue(name, value, dbType, isList));
            return this;
        }

        /// <summary>
        /// Leaves @name text without a value unchanged instead of rejecting it.
        /// Use it for SQL that declares its own variables, such as T-SQL DECLARE or MySQL session variables.
        /// A supplied value without a marker is still rejected.
        /// </summary>
        /// <returns>This query.</returns>
        public RawQuery IgnoreUnmatchedMarkers()
        {
            _ignoreUnmatchedMarkers = true;
            return this;
        }

        /// <summary>
        /// Runs the command without the transaction that Execute and ExecuteScalar otherwise open on a connection they own.
        /// Use it for reads, and for statements that cannot run inside a transaction.
        /// </summary>
        /// <returns>This query.</returns>
        public RawQuery WithoutTransaction()
        {
            _withoutTransaction = true;
            return this;
        }

        /// <summary>
        /// Replaces each @name marker with a parameter bound to its value. Markers inside string literals, quoted identifiers, and comments are left alone.
        /// </summary>
        /// <param name="existingCollection">The collection receiving the bindings.</param>
        /// <param name="flavor">The target database, which selects the placeholder syntax.</param>
        /// <returns>The parameterized SQL text.</returns>
        /// <exception cref="InvalidOperationException">A marker has no value, a value has no marker, or a list value is empty.</exception>
        public override string Render(ParameterCollection existingCollection, DbFlavor flavor)
        {
            existingCollection.SetFlavor(flavor);
            var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string text = SqlMarkers.Replace(_sql, flavor, name =>
            {
                if (placeholders.TryGetValue(name, out string placeholder)) return placeholder;
                var value = _values.Find(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (value == null)
                {
                    if (_ignoreUnmatchedMarkers) return "@" + name;
                    throw new InvalidOperationException($"SQL marker @{name} has no value.");
                }
                if (value.IsList)
                {
                    var items = (object[])value.Value;
                    if (items.Length == 0)
                        throw new InvalidOperationException($"SQL list value '{value.Name}' is empty. Decide what the statement means without values before running it.");
                    var names = new string[items.Length];
                    for (int i = 0; i < items.Length; i++)
                        names[i] = existingCollection.AddNamedParameter(value.Name + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), items[i], value.DbType);
                    placeholder = string.Join(", ", names);
                }
                else placeholder = existingCollection.AddNamedParameter(value.Name, value.Value, value.DbType);
                placeholders.Add(name, placeholder);
                return placeholder;
            });
            foreach (var value in _values)
                if (!placeholders.ContainsKey(value.Name))
                    throw new InvalidOperationException($"SQL value '{value.Name}' has no @{value.Name} marker.");
            return text;
        }

        private static bool IsMarkerName(string name)
        {
            if (name.Length == 0 || !SqlMarkers.IsNameStart(name[0])) return false;
            foreach (char c in name)
                if (!SqlMarkers.IsNamePart(c)) return false;
            return true;
        }
    }
}
