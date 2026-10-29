using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Compilation
{
    // Compatibility adapter for the existing public, mutable query graph. Field plans
    // are discovered once per runtime type, including user-derived query nodes. Identity
    // tracking preserves shared CTEs, column references, and model relationship cycles.
    // The snapshot owns its graph; every compilation receives a separate working graph.
    internal sealed class SnapshotGraph
    {
        private static readonly Func<object, object> ShallowCopy = CreateShallowCopy();
        private static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new ConcurrentDictionary<Type, FieldInfo[]>();
        private readonly Dictionary<object, object> copies = new Dictionary<object, object>(ReferenceComparer.Instance);

        internal static T Capture<T>(T value) where T : class => (T)new SnapshotGraph().Copy(value);

        // Callable preparation creates fresh bindings. Reuse private generated tables and snapshot their mutable cells;
        // borrowed tables and scalar arrays still receive independent copies through the same identity-tracking graph.
        internal static void CaptureCallableInputs(SqlLinqer.Components.Render.RenderedQuery query)
        {
            var graph = new SnapshotGraph();
            foreach (var table in query.OwnedTables)
            {
                graph.copies.Add(table, table);
                graph.CopyTableCells(table, table);
            }
            query.Parameters = (SqlLinqer.Components.Render.ParameterCollection)graph.Copy(query.Parameters);
            foreach (var parameter in query.Parameters.GetAll())
                if (parameter.Value is DataTable table) query.OwnTable(table);
        }

        private void CopyTableCells(DataTable source, DataTable destination)
        {
            // DataTable.Copy retains reference-valued cells.
            for (int row = 0; row < destination.Rows.Count; row++)
                for (int column = 0; column < destination.Columns.Count; column++)
                {
                    var cell = source.Rows[row][column];
                    var copied = Copy(cell);
                    if (ReferenceEquals(cell, copied)) continue;
                    var definition = destination.Columns[column];
                    bool readOnly = definition.ReadOnly;
                    if (readOnly) definition.ReadOnly = false;
                    try { destination.Rows[row][column] = copied; }
                    finally { if (readOnly) definition.ReadOnly = true; }
                }
        }

        private object Copy(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsValueType || value is string || value is Type || value is MemberInfo || value is Delegate)
                return value;
            if (copies.TryGetValue(value, out var existing)) return existing;

            if (value is DataTable table)
            {
                var result = table.Copy();
                copies.Add(value, result);
                CopyTableCells(table, result);
                return result;
            }
            if (value is Array array)
            {
                var result = (Array)array.Clone();
                copies.Add(value, result);
                if (!type.GetElementType().IsValueType)
                {
                    if (array.Rank == 1)
                        for (int i = array.GetLowerBound(0); i <= array.GetUpperBound(0); i++)
                            result.SetValue(Copy(array.GetValue(i)), i);
                    else CopyArray(array, result, new int[array.Rank], 0);
                }
                return result;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var comparer = type.GetProperty("Comparer").GetValue(value);
                var result = (IDictionary)Activator.CreateInstance(type, comparer);
                copies.Add(value, result);
                foreach (DictionaryEntry item in (IDictionary)value)
                    result.Add(Copy(item.Key), Copy(item.Value));
                return result;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var result = (IList)Activator.CreateInstance(type);
                copies.Add(value, result);
                foreach (var item in (IList)value) result.Add(Copy(item));
                return result;
            }
            if (value is IEnumerable<Column> columns && !(value is ColumnCollection))
            {
                var result = columns.Select(c => (Column)Copy(c)).ToArray();
                copies.Add(value, result);
                return result;
            }

            bool queryNode = value is BaseQuery || value is SelectStatement || value is IWhereComponent
                || value is SqlLinqer.Components.Ordering.IOrderByStatement || value is SqlLinqer.Components.Outputs.IOutputStatement;
            for (var owner = type; !queryNode && owner != null; owner = owner.BaseType)
                queryNode = owner.Assembly == typeof(BaseQuery).Assembly
                    && (owner.Namespace.StartsWith("SqlLinqer.Components.", StringComparison.Ordinal)
                        || owner.Namespace.StartsWith("SqlLinqer.Modeling", StringComparison.Ordinal));
            // Provider-specific scalar values and runtime dependencies remain opaque.
            // They must be immutable for the lifetime of a snapshot, as documented.
            if (!queryNode) return value;

            var clone = ShallowCopy(value);
            copies.Add(value, clone);
            foreach (var field in Fields.GetOrAdd(type, GetFields))
                field.SetValue(clone, Copy(field.GetValue(value)));
            if (clone is BaseQuery query) query.IsCompilationCopy = true;
            if (clone is SqlLinqer.Components.Insert.InsertRowCollection inserts) inserts.CaptureValues(Copy);
            return clone;
        }

        private void CopyArray(Array source, Array destination, int[] indices, int dimension)
        {
            for (int i = source.GetLowerBound(dimension); i <= source.GetUpperBound(dimension); i++)
            {
                indices[dimension] = i;
                if (dimension + 1 == source.Rank) destination.SetValue(Copy(source.GetValue(indices)), indices);
                else CopyArray(source, destination, indices, dimension + 1);
            }
        }

        private static FieldInfo[] GetFields(Type type)
        {
            var fields = new List<FieldInfo>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.FieldType.IsValueType && f.FieldType != typeof(string)));
            return fields.ToArray();
        }

        private static Func<object, object> CreateShallowCopy()
        {
            var value = Expression.Parameter(typeof(object));
            var method = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            return Expression.Lambda<Func<object, object>>(Expression.Call(value, method), value).Compile();
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
